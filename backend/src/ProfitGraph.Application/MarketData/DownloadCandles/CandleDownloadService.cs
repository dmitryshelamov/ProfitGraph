using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.DownloadCandles;

/// <summary>
///     Brings a series of closed candles into the cache (ADR 0010, Profit Fox Designer ADR 0017). The clock is read once;
///     only missing ranges are fetched; every chunk is checked completely and stored in its own short transaction, so a
///     failure keeps the chunks already stored and a later download continues from the cache.
/// </summary>
public sealed class CandleDownloadService(
    VenueRegistry venues,
    VenuePorts<IHistoricalCandleSource> sources,
    ICandleRepository repository,
    ITransactionExecutor transactions,
    IBackgroundJobProgress progress,
    HistoryDownloadOptions options,
    TimeProvider clock)
{
    public async Task<HistoricalDownloadInternal> Download(DownloadCandlesCommand request, CancellationToken cancellationToken)
    {
        CandleSeries series = venues.ResolveSeries(request.Venue, request.Symbol, request.Timeframe);
        ResolvedCandleRange range = await Resolve(series, request.From, request.To, clock.GetUtcNow(), cancellationToken);

        return await Ensure(range, reportProgress: true, cancellationToken);
    }

    /// <summary>
    ///     Fixes the range against one clock reading: an omitted start is the venue's first candle, an omitted or later end
    ///     is the latest closed candle.
    /// </summary>
    public async Task<ResolvedCandleRange> Resolve(
        CandleSeries series,
        DateTimeOffset? requestedFrom,
        DateTimeOffset? requestedTo,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        transactions.EnsureNoTransaction();
        Timeframe timeframe = series.Timeframe;
        DateTimeOffset latest = timeframe.FloorToGrid(observedAt);

        if (requestedFrom >= latest)
        {
            throw new ApplicationValidationException([new InputError("invalid_request", "From", "The start must come before the latest closed candle.")]);
        }

        DateTimeOffset to = requestedTo is null || requestedTo > latest ? latest : requestedTo.Value;
        DateTimeOffset from = requestedFrom ?? await Source(series).FindFirst(series.Symbol, timeframe, to, cancellationToken)
            ?? throw new HistoricalDataException(
                HistoricalDataErrorCodes.Unavailable,
                "The venue has no closed history before the requested end.",
                [new CandleRangeInternal(DateTimeOffset.UnixEpoch, to)]);

        if (from.Offset != TimeSpan.Zero || from < DateTimeOffset.UnixEpoch || !timeframe.IsAligned(from))
        {
            throw new HistoricalDataException(
                HistoricalDataErrorCodes.InvalidProviderData,
                "The venue reported an invalid history start.",
                [new CandleRangeInternal(from, to)]);
        }

        return to > from
            ? new ResolvedCandleRange(series, from, to, observedAt, latest)
            : throw new ApplicationValidationException([new InputError("invalid_request", "To", "The range must contain at least one closed candle.")]);
    }

    /// <summary>
    ///     Brings every candle of a resolved range into the cache without reading the clock again; the venue is asked only
    ///     for missing candles.
    /// </summary>
    public async Task<HistoricalDownloadInternal> Ensure(ResolvedCandleRange range, bool reportProgress, CancellationToken cancellationToken)
    {
        transactions.EnsureNoTransaction();
        CandleSeries series = range.Series;
        long total = range.Count;
        long cached = 0;
        long downloaded = 0;
        long inserted = 0;

        if (reportProgress)
        {
            await progress.Report(completed: 0, total, cancellationToken);
        }

        for (DateTimeOffset chunkFrom = range.From; chunkFrom < range.To;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long count = Math.Min(options.MaxCandlesPerChunk, (range.To.UtcTicks - chunkFrom.UtcTicks) / series.Timeframe.Duration.Ticks);
            DateTimeOffset chunkTo = chunkFrom.AddTicks(count * series.Timeframe.Duration.Ticks);
            (int chunkCached, int chunkDownloaded, int chunkInserted) = await Chunk(series, chunkFrom, chunkTo, (int)count, range.ObservedAt, cancellationToken);
            cached += chunkCached;
            downloaded += chunkDownloaded;
            inserted += chunkInserted;
            chunkFrom = chunkTo;

            if (reportProgress)
            {
                await progress.Report((chunkFrom.UtcTicks - range.From.UtcTicks) / series.Timeframe.Duration.Ticks, total, cancellationToken);
            }
        }

        return new HistoricalDownloadInternal(series.Venue.Value, series.Symbol.Value, series.Timeframe.Key, range.From, range.To, total, cached, downloaded, inserted);
    }

    private IHistoricalCandleSource Source(CandleSeries series)
    {
        return sources.Find(series.Venue)
            ?? throw new HistoricalDataException(HistoricalDataErrorCodes.Unavailable, "This venue offers no public history download.", []);
    }

    private async Task<(int Cached, int Downloaded, int Inserted)> Chunk(
        CandleSeries series,
        DateTimeOffset from,
        DateTimeOffset to,
        int count,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var cachedCandles = new Dictionary<DateTimeOffset, Candle>();
        AddChecked(
            await repository.Get(series, from, to, count, cancellationToken),
            cachedCandles,
            series.Timeframe,
            from,
            to,
            HistoricalDataErrorCodes.InvalidStoredData);
        IReadOnlyList<CandleRangeInternal> missing = FindMissing(cachedCandles, series.Timeframe, from, to);

        // A fully cached chunk needs no venue, so history imported for a venue without downloads can still be prepared.
        if (missing.Count == 0)
        {
            return (cachedCandles.Count, 0, 0);
        }

        IHistoricalCandleSource source = Source(series);
        var downloadedCandles = new Dictionary<DateTimeOffset, Candle>();

        foreach (CandleRangeInternal range in missing)
        {
            IReadOnlyList<Candle> candles = await source.Get(series.Symbol, series.Timeframe, range.From, range.To, cancellationToken);
            AddChecked(candles, downloadedCandles, series.Timeframe, range.From, range.To, HistoricalDataErrorCodes.InvalidProviderData);
        }

        CandleRangeInternal[] gaps = [.. missing.SelectMany(range => FindMissing(downloadedCandles, series.Timeframe, range.From, range.To))];

        if (gaps.Length > 0)
        {
            throw new HistoricalDataException(
                HistoricalDataErrorCodes.Unavailable,
                "The venue's history is incomplete; nothing from the failing chunk was stored, earlier chunks are kept.",
                gaps);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Candle[] downloaded = [.. downloadedCandles.Values.OrderBy(candle => candle.OpenTime)];
        int inserted = await transactions.ExecuteAsync(() => repository.Append(series, downloaded, source.Source, observedAt, cancellationToken), cancellationToken);

        return (cachedCandles.Count, downloadedCandles.Count, inserted);
    }

    private static List<CandleRangeInternal> FindMissing(Dictionary<DateTimeOffset, Candle> candles, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        var ranges = new List<CandleRangeInternal>();
        DateTimeOffset? start = null;

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            if (!candles.ContainsKey(time))
            {
                start ??= time;
            }
            else if (start is not null)
            {
                ranges.Add(new CandleRangeInternal(start.Value, time));
                start = null;
            }
        }

        if (start is not null)
        {
            ranges.Add(new CandleRangeInternal(start.Value, to));
        }

        return ranges;
    }

    private static void AddChecked(
        IReadOnlyList<Candle> candles,
        Dictionary<DateTimeOffset, Candle> all,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        string invalidDataCode)
    {
        foreach (Candle candle in candles)
        {
            if (!CandleValidator.IsValid(candle, timeframe) || candle.OpenTime < from || candle.OpenTime >= to)
            {
                throw new HistoricalDataException(invalidDataCode, "The history contains invalid or out-of-range candles.", [new CandleRangeInternal(from, to)]);
            }

            if (all.TryGetValue(candle.OpenTime, out Candle? previous) && previous != candle)
            {
                throw new HistoricalDataException(
                    invalidDataCode == HistoricalDataErrorCodes.InvalidStoredData ? invalidDataCode : HistoricalDataErrorCodes.Conflict,
                    "The history contains conflicting candles.",
                    [new CandleRangeInternal(candle.OpenTime, candle.OpenTime + timeframe.Duration)]);
            }

            all[candle.OpenTime] = candle;
        }
    }
}