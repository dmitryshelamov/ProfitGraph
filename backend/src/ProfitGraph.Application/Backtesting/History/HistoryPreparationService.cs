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
///     out, checking that every grid interval is a candle or lies in a market closure (ADR 0023, TC1). Warm-up counts
///     candles, so closures move its start further back.
/// </summary>
public sealed class HistoryPreparationService(
    CandleDownloadService downloads,
    VenuePorts<IHistoricalCandleSource> sources,
    ICandleRepository repository,
    HistoryDownloadOptions options)
{
    public const int MaxWarmupCandles = 100_000;

    /// <summary>
    ///     How often the warm-up may reach further back when closures leave it short; each step at least doubles its span.
    /// </summary>
    private const int MaxWarmupExtensions = 16;

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
        (DateTimeOffset historyFrom, DateTimeOffset? first) = await Warmup(replay, warmupCandles, cancellationToken);
        await downloads.Ensure(replay with { From = historyFrom }, reportProgress, cancellationToken);
        CopiedHistory copied = await Copy(series, historyFrom, replay.To, cancellationToken);

        if (copied.Closures.Count > 0 && WarmupCount(copied, replay.From) < warmupCandles)
        {
            copied = await ExtendWarmup(replay, warmupCandles, first, copied, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        int warmupCount = WarmupCount(copied, replay.From);

        return copied.Candles.Count > warmupCount
            ? new PreparedHistory(replay, copied.From, warmupCount, [.. copied.Candles], copied.Closures)
            : throw Invalid(HistoricalDataErrorCodes.InvalidStoredData, copied.From, replay.To, "Prepared history has no candle to replay.");
    }

    /// <summary>
    ///     Where the warm-up starts on a contiguous grid, and the venue's first candle when the venue was asked. A warm-up
    ///     already complete in the cache (for example after a CSV import) needs no venue request; otherwise it is clipped to
    ///     the venue's first candle.
    /// </summary>
    private async Task<(DateTimeOffset From, DateTimeOffset? First)> Warmup(ResolvedCandleRange replay, int requested, CancellationToken cancellationToken)
    {
        Timeframe timeframe = replay.Series.Timeframe;
        long grid = (replay.From.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / timeframe.Duration.Ticks;

        if (requested == 0 || grid == 0)
        {
            return (replay.From, null);
        }

        if (requested <= grid)
        {
            DateTimeOffset cachedFrom = replay.From.AddTicks(-requested * timeframe.Duration.Ticks);
            IReadOnlyList<Candle> cached = await repository.Get(replay.Series, cachedFrom, replay.From, requested, cancellationToken);

            if (cached.Count == requested)
            {
                return (cachedFrom, null);
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
            return (replay.From, null);
        }

        if (first.Value.Offset != TimeSpan.Zero || first < DateTimeOffset.UnixEpoch || first >= replay.From || !timeframe.IsAligned(first.Value))
        {
            throw Invalid(HistoricalDataErrorCodes.InvalidProviderData, first.Value, replay.From, "The venue reported an invalid warm-up start.");
        }

        long count = Math.Min(requested, (replay.From.UtcTicks - first.Value.UtcTicks) / timeframe.Duration.Ticks);

        return (replay.From.AddTicks(-count * timeframe.Duration.Ticks), first);
    }

    /// <summary>
    ///     Reaches further back until the warm-up has the requested candles or starts at the venue's first candle, then
    ///     drops what lies before the requested count.
    /// </summary>
    private async Task<CopiedHistory> ExtendWarmup(
        ResolvedCandleRange replay,
        int requested,
        DateTimeOffset? first,
        CopiedHistory copied,
        CancellationToken cancellationToken)
    {
        Timeframe timeframe = replay.Series.Timeframe;

        for (var extension = 0;; extension++)
        {
            int have = WarmupCount(copied, replay.From);

            if (have >= requested)
            {
                return Trim(copied, have - requested);
            }

            if (first is null || copied.From <= first)
            {
                return copied;
            }

            if (extension == MaxWarmupExtensions)
            {
                throw Invalid(HistoricalDataErrorCodes.Unavailable, first.Value, replay.From, "The warm-up was not found within the allowed range.");
            }

            long span = Math.Max(requested - have, (replay.From.UtcTicks - copied.From.UtcTicks) / timeframe.Duration.Ticks);
            long available = (copied.From.UtcTicks - first.Value.UtcTicks) / timeframe.Duration.Ticks;
            DateTimeOffset earlier = copied.From.AddTicks(-Math.Min(span, available) * timeframe.Duration.Ticks);
            await downloads.Ensure(replay with { From = earlier, To = copied.From }, reportProgress: false, cancellationToken);
            CopiedHistory prefix = await Copy(replay.Series, earlier, copied.From, cancellationToken);
            copied = new CopiedHistory(
                earlier,
                [.. prefix.Candles, .. copied.Candles],
                CandleCoverage.Merge([.. prefix.Closures, .. copied.Closures], earlier, replay.To));
        }
    }

    private static CopiedHistory Trim(CopiedHistory copied, int drop)
    {
        if (drop == 0)
        {
            return copied;
        }

        DateTimeOffset from = copied.Candles[drop].OpenTime;

        return new CopiedHistory(from, [.. copied.Candles.Skip(drop)], CandleCoverage.Merge(copied.Closures, from, DateTimeOffset.MaxValue));
    }

    private static int WarmupCount(CopiedHistory copied, DateTimeOffset replayFrom)
    {
        return copied.Candles.TakeWhile(candle => candle.OpenTime < replayFrom).Count();
    }

    private async Task<CopiedHistory> Copy(CandleSeries series, DateTimeOffset historyFrom, DateTimeOffset to, CancellationToken cancellationToken)
    {
        long step = series.Timeframe.Duration.Ticks;
        var candles = new List<Candle>();
        var closures = new List<CandleRangeInternal>();

        for (DateTimeOffset chunkFrom = historyFrom; chunkFrom < to;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(options.MaxCandlesPerChunk, (to.UtcTicks - chunkFrom.UtcTicks) / step);
            DateTimeOffset chunkTo = chunkFrom.AddTicks(count * step);
            IReadOnlyList<Candle> stored = await repository.Get(series, chunkFrom, chunkTo, count, cancellationToken);
            List<CandleRangeInternal> closed = CandleCoverage.Merge(await repository.GetClosures(series, chunkFrom, chunkTo, cancellationToken), chunkFrom, chunkTo);

            if (stored.Any(candle => !CandleValidator.IsValid(candle, series.Timeframe)) || CandleCoverage.Overlaps(stored.Select(candle => candle.OpenTime), closed))
            {
                throw Invalid(HistoricalDataErrorCodes.InvalidStoredData, chunkFrom, chunkTo, "The cached history is invalid.");
            }

            if (CandleCoverage.FindMissing(stored.Select(candle => candle.OpenTime).ToHashSet(), closed, series.Timeframe, chunkFrom, chunkTo).Count > 0)
            {
                throw Invalid(HistoricalDataErrorCodes.InvalidStoredData, chunkFrom, chunkTo, "The cached history is incomplete.");
            }

            candles.AddRange(stored);
            closures.AddRange(closed);
            chunkFrom = chunkTo;
        }

        return new CopiedHistory(historyFrom, candles, CandleCoverage.Merge(closures, historyFrom, to));
    }

    private static HistoricalDataException Invalid(string code, DateTimeOffset from, DateTimeOffset to, string message)
    {
        return new HistoricalDataException(code, message, [new CandleRangeInternal(from, to)]);
    }
}