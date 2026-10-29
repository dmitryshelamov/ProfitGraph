using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Charts;

/// <summary>
///     Reads a page of a series' chart (ADR 0026). From is rounded down and To up to the timeframe grid, and To is cut at the
///     latest closed candle. Missing candles of the page are brought into the shared cache from the venue first, like a history
///     download of one chunk. When the venue's history has intervals without a candle (an hour without sales), the venue's
///     candles are shown without being stored; when the venue cannot be read, the stored candles are shown. A venue without
///     anonymous market data reads with the access the caller opened (<see cref="MarketDataAccess" />).
/// </summary>
public sealed class CandleChartReader(
    CandleDownloadService downloads,
    ICandleRepository candles,
    VenuePorts<IHistoricalCandleSource> sources,
    TimeProvider clock)
{
    public async Task<CandleChartPage> Read(
        CandleSeries series,
        DateTimeOffset requestedFrom,
        DateTimeOffset requestedTo,
        int maxCandles,
        CancellationToken cancellationToken)
    {
        Timeframe timeframe = series.Timeframe;
        DateTimeOffset observedAt = clock.GetUtcNow();
        DateTimeOffset latest = timeframe.FloorToGrid(observedAt);
        DateTimeOffset from = timeframe.FloorToGrid(requestedFrom);
        DateTimeOffset end = timeframe.FloorToGrid(requestedTo);
        DateTimeOffset to = Min(end == requestedTo ? end : end + timeframe.Duration, latest);

        if ((to.UtcTicks - from.UtcTicks) / timeframe.Duration.Ticks > maxCandles)
        {
            throw new ApplicationValidationException([new InputError("invalid_value", "To", $"A page covers at most {maxCandles} candles of the chart's timeframe.")]);
        }

        if (to <= from)
        {
            return new CandleChartPage(from, from, latest, CandleHistoryStatus.Complete, ErrorCode: null, []);
        }

        var count = (int)((to.UtcTicks - from.UtcTicks) / timeframe.Duration.Ticks);

        try
        {
            await downloads.Ensure(new ResolvedCandleRange(series, from, to, observedAt, latest), reportProgress: false, cancellationToken);

            return new CandleChartPage(from, to, latest, CandleHistoryStatus.Complete, ErrorCode: null, await candles.Get(series, from, to, count, cancellationToken));
        }
        catch (HistoricalDataException exception) when (exception.Code == HistoricalDataErrorCodes.Unavailable && sources.Find(series.Venue) is not null)
        {
            (CandleHistoryStatus status, string? errorCode, IReadOnlyList<Candle> page) = await ReadIncomplete(series, from, to, count, cancellationToken);

            return new CandleChartPage(from, to, latest, status, errorCode, page);
        }
        catch (HistoricalDataException exception)
        {
            return new CandleChartPage(from, to, latest, CandleHistoryStatus.Unavailable, exception.Code, await candles.Get(series, from, to, count, cancellationToken));
        }
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }

    /// <summary>
    ///     The stored candles with the venue's own candles of the intervals the cache lacks. The venue's candles are not
    ///     stored: the cache keeps only ranges the venue covers completely, which backtests rely on.
    /// </summary>
    private async Task<(CandleHistoryStatus Status, string? ErrorCode, IReadOnlyList<Candle> Candles)> ReadIncomplete(
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

            return (CandleHistoryStatus.Partial, null, [.. merged.Values]);
        }
        catch (HistoricalDataException exception)
        {
            return (CandleHistoryStatus.Unavailable, exception.Code, stored);
        }
    }
}