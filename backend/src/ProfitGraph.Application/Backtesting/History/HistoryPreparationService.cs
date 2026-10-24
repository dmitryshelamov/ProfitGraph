using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Backtesting.History;

/// <summary>
///     Prepares the history of one run (Profit Fox Designer ADR 0021): fixes the range against one clock reading, adds as
///     much warm-up as the venue has (up to the requested count), brings everything into the candle cache and copies it
///     out, checking that it is complete and contiguous.
/// </summary>
public sealed class HistoryPreparationService(
    CandleDownloadService downloads,
    VenuePorts<IHistoricalCandleSource> sources,
    ICandleRepository repository,
    HistoryDownloadOptions options)
{
    public const int MaxWarmupCandles = 100_000;

    public async Task<PreparedHistory> Prepare(
        CandleSeries series,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int warmupCandles,
        DateTimeOffset observedAt,
        bool reportProgress,
        CancellationToken cancellationToken)
    {
        if (warmupCandles is < 0 or > MaxWarmupCandles)
        {
            throw new ApplicationValidationException([new InputError("invalid_request", "WarmupCandles", $"Warm-up must be between 0 and {MaxWarmupCandles} candles.")]);
        }

        ResolvedCandleRange replay = await downloads.Resolve(series, from, to, observedAt, cancellationToken);
        (DateTimeOffset historyFrom, int warmupCount) = await Warmup(replay, warmupCandles, cancellationToken);
        await downloads.Ensure(replay with { From = historyFrom }, reportProgress, cancellationToken);
        Candle[] candles = await Copy(series, historyFrom, replay.To, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return candles.Length > warmupCount
            ? new PreparedHistory(replay, historyFrom, warmupCount, candles)
            : throw Invalid(HistoricalDataErrorCodes.InvalidStoredData, historyFrom, replay.To, "Prepared history has no candle to replay.");
    }

    /// <summary>
    ///     A warm-up already complete in the cache (for example after a CSV import) needs no venue request; otherwise it is
    ///     clipped to the venue's first candle.
    /// </summary>
    private async Task<(DateTimeOffset From, int Count)> Warmup(ResolvedCandleRange replay, int requested, CancellationToken cancellationToken)
    {
        Timeframe timeframe = replay.Series.Timeframe;
        long grid = (replay.From.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / timeframe.Duration.Ticks;

        if (requested == 0 || grid == 0)
        {
            return (replay.From, 0);
        }

        if (requested <= grid)
        {
            DateTimeOffset cachedFrom = replay.From.AddTicks(-requested * timeframe.Duration.Ticks);
            IReadOnlyList<Candle> cached = await repository.Get(replay.Series, cachedFrom, replay.From, requested, cancellationToken);

            if (cached.Count == requested)
            {
                return (cachedFrom, requested);
            }
        }

        IHistoricalCandleSource source = sources.Find(replay.Series.Venue)
            ?? throw Invalid(
                HistoricalDataErrorCodes.Unavailable,
                DateTimeOffset.UnixEpoch,
                replay.From,
                "The cache lacks the warm-up and this venue offers no history download.");
        DateTimeOffset? first = await source.FindFirst(replay.Series.Symbol, timeframe, replay.From, cancellationToken);

        if (first is null)
        {
            return (replay.From, 0);
        }

        if (first.Value.Offset != TimeSpan.Zero || first < DateTimeOffset.UnixEpoch || first >= replay.From || !timeframe.IsAligned(first.Value))
        {
            throw Invalid(HistoricalDataErrorCodes.InvalidProviderData, first.Value, replay.From, "The venue reported an invalid warm-up start.");
        }

        var count = (int)Math.Min(requested, (replay.From.UtcTicks - first.Value.UtcTicks) / timeframe.Duration.Ticks);

        return (replay.From.AddTicks(-count * timeframe.Duration.Ticks), count);
    }

    private async Task<Candle[]> Copy(CandleSeries series, DateTimeOffset historyFrom, DateTimeOffset to, CancellationToken cancellationToken)
    {
        long step = series.Timeframe.Duration.Ticks;
        var candles = new Candle[checked((int)((to.UtcTicks - historyFrom.UtcTicks) / step))];
        var index = 0;

        for (DateTimeOffset chunkFrom = historyFrom; chunkFrom < to;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(options.MaxCandlesPerChunk, (to.UtcTicks - chunkFrom.UtcTicks) / step);
            DateTimeOffset chunkTo = chunkFrom.AddTicks(count * step);
            IReadOnlyList<Candle> stored = await repository.Get(series, chunkFrom, chunkTo, count, cancellationToken);

            if (stored.Count != count)
            {
                throw Invalid(HistoricalDataErrorCodes.InvalidStoredData, chunkFrom, chunkTo, "The cached history is incomplete.");
            }

            foreach (Candle candle in stored)
            {
                if (!CandleValidator.IsValid(candle, series.Timeframe) || candle.OpenTime != historyFrom.AddTicks(index * step))
                {
                    throw Invalid(HistoricalDataErrorCodes.InvalidStoredData, chunkFrom, chunkTo, "The cached history is invalid or unordered.");
                }

                candles[index++] = candle;
            }

            chunkFrom = chunkTo;
        }

        return candles;
    }

    private static HistoricalDataException Invalid(string code, DateTimeOffset from, DateTimeOffset to, string message)
    {
        return new HistoricalDataException(code, message, [new CandleRangeInternal(from, to)]);
    }
}