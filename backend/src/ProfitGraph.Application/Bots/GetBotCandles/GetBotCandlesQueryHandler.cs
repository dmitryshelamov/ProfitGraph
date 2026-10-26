using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Converters;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.GetBotCandles;

/// <summary>
///     Reads a page of the bot's chart (ADR 0026). Missing candles of the page are brought into the shared cache from the
///     venue with the bot's market data access first, like a history download of one chunk. When the venue's history has
///     intervals without a candle (an hour without sales), the venue's candles are shown without being stored; when
///     the venue cannot be read, the stored candles are shown. Fills come from the bot's record for the same range.
/// </summary>
internal sealed class GetBotCandlesQueryHandler(
    IBotRepository bots,
    IBotFillRepository fills,
    BotMarketDataAccess access,
    CandleDownloadService downloads,
    ICandleRepository candles,
    VenuePorts<IHistoricalCandleSource> sources,
    TimeProvider clock) : IRequestHandler<GetBotCandlesQuery, BotCandlesInternal>
{
    /// <summary>
    ///     The most candles one page covers: one venue read of history per page at most.
    /// </summary>
    public const int MaxCandles = 1000;

    public const int MaxFills = 1000;

    public async Task<BotCandlesInternal> Handle(GetBotCandlesQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        CandleSeries series = bot.Series;
        Timeframe timeframe = series.Timeframe;
        DateTimeOffset observedAt = clock.GetUtcNow();
        DateTimeOffset latest = timeframe.FloorToGrid(observedAt);
        DateTimeOffset from = timeframe.FloorToGrid(query.From);
        DateTimeOffset end = timeframe.FloorToGrid(query.To);
        DateTimeOffset to = Min(end == query.To ? end : end + timeframe.Duration, latest);

        if ((to.UtcTicks - from.UtcTicks) / timeframe.Duration.Ticks > MaxCandles)
        {
            throw new ApplicationValidationException([new InputError("invalid_value", "To", $"A page covers at most {MaxCandles} candles of the bot's timeframe.")]);
        }

        if (to <= from)
        {
            return Page(bot, from, from, latest, BotCandleHistoryStatus.Complete, errorCode: null, [], [], fillsTruncated: false);
        }

        (BotCandleHistoryStatus status, string? errorCode, IReadOnlyList<Candle> page) = await Read(bot, from, to, observedAt, latest, cancellationToken);
        IReadOnlyList<BotFill> made = await fills.ListFilled(bot.Id, from, to, MaxFills + 1, cancellationToken);
        BotChartFillInternal[] marked =
        [
            .. made.Take(MaxFills).Select(fill => new BotChartFillInternal(fill.Id.Value, fill.FilledAt, fill.Side, fill.Price, fill.GrossBase, fill.Tag))
        ];

        return Page(bot, from, to, latest, status, errorCode, [.. page.Select(candle => candle.ToInternal())], marked, made.Count > MaxFills);
    }

    private static BotCandlesInternal Page(
        Bot bot,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset latest,
        BotCandleHistoryStatus status,
        string? errorCode,
        IReadOnlyList<CandleInternal> page,
        IReadOnlyList<BotChartFillInternal> marked,
        bool fillsTruncated)
    {
        return new BotCandlesInternal(
            bot.Series.Venue.Value,
            bot.Series.Symbol.Value,
            bot.Series.Timeframe.Key,
            from,
            to,
            latest,
            status,
            errorCode,
            page,
            marked,
            fillsTruncated);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }

    private async Task<(BotCandleHistoryStatus Status, string? ErrorCode, IReadOnlyList<Candle> Candles)> Read(
        Bot bot,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset observedAt,
        DateTimeOffset latest,
        CancellationToken cancellationToken)
    {
        CandleSeries series = bot.Series;
        var count = (int)((to.UtcTicks - from.UtcTicks) / series.Timeframe.Duration.Ticks);

        // A venue without anonymous market data is read with the bot's own credential (ADR 0023, TC6).
        using IDisposable scope = MarketDataAccess.Use(await access.Read(bot, cancellationToken));

        try
        {
            await downloads.Ensure(new ResolvedCandleRange(series, from, to, observedAt, latest), reportProgress: false, cancellationToken);

            return (BotCandleHistoryStatus.Complete, null, await candles.Get(series, from, to, count, cancellationToken));
        }
        catch (HistoricalDataException exception) when (exception.Code == HistoricalDataErrorCodes.Unavailable && sources.Find(series.Venue) is not null)
        {
            return await ReadIncomplete(series, from, to, count, cancellationToken);
        }
        catch (HistoricalDataException exception)
        {
            return (BotCandleHistoryStatus.Unavailable, exception.Code, await candles.Get(series, from, to, count, cancellationToken));
        }
    }

    /// <summary>
    ///     The stored candles with the venue's own candles of the intervals the cache lacks. The venue's candles are not
    ///     stored: the cache keeps only ranges the venue covers completely, which backtests rely on.
    /// </summary>
    private async Task<(BotCandleHistoryStatus Status, string? ErrorCode, IReadOnlyList<Candle> Candles)> ReadIncomplete(
        CandleSeries series,
        DateTimeOffset from,
        DateTimeOffset to,
        int count,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Candle> stored = await candles.Get(series, from, to, count, cancellationToken);

        try
        {
            HistoricalCandlesInternal history = await sources.Find(series.Venue)!.Get(series.Symbol, series.Timeframe, from, to, cancellationToken);
            var merged = new SortedDictionary<DateTimeOffset, Candle>();

            foreach (Candle candle in history.Candles.Where(candle =>
                         CandleValidator.IsValid(candle, series.Timeframe) && candle.OpenTime >= from && candle.OpenTime < to))
            {
                merged[candle.OpenTime] = candle;
            }

            foreach (Candle candle in stored)
            {
                merged[candle.OpenTime] = candle;
            }

            return (BotCandleHistoryStatus.Partial, null, [.. merged.Values]);
        }
        catch (HistoricalDataException exception)
        {
            return (BotCandleHistoryStatus.Unavailable, exception.Code, stored);
        }
    }
}