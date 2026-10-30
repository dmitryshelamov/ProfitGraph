using System.Collections.Concurrent;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Application.Scanner.Runtime;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     Scans one venue (ADR 0059, section 2): its universe in streams of <see cref="IMarketScanSource.MaxSymbolsPerStream" /> pairs,
///     each stream subscribed before its pairs read their history, a REST backfill after every reconnect, and after each minute a
///     snapshot of every shown pair, each row with the trend of the venue's reference pair (ADR 0071, section 3). No snapshot is written before the first pass over every stream, so a
///     restart keeps showing the
///     last stored one while it warms up.
/// </summary>
internal sealed class VenueScanRunner(
    IMarketScanSource source,
    IServiceScopeFactory scopes,
    ScannerOptions options,
    TimeProvider clock,
    ILogger logger)
{
    private static readonly Timeframe MinuteFrame = Timeframe.Parse("1m");
    private static readonly Timeframe HourFrame = Timeframe.Parse("1h");
    private static readonly TimeSpan MinuteHistory = TimeSpan.FromMinutes(ScanSeries.MinuteCapacity);
    private static readonly TimeSpan HourHistory = TimeSpan.FromHours(ScanSeries.HourCapacity);

    private readonly ConcurrentDictionary<string, ScanSeries> _series = new ConcurrentDictionary<string, ScanSeries>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset?> _listedAt = new ConcurrentDictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
    private volatile bool _ready;

    private string Venue => source.Venue.Value;

    public async Task Run(CancellationToken cancellationToken)
    {
        Task snapshots = RunSnapshots(cancellationToken);
        HashSet<string> current = [];
        CancellationTokenSource? streams = null;
        var running = Task.CompletedTask;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await LoadUniverse(cancellationToken) is { } universe && !universe.SetEquals(current))
                {
                    if (streams is not null)
                    {
                        await streams.CancelAsync();
                        await Settle(running);
                        streams.Dispose();
                    }

                    current = universe;
                    streams = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    running = Start(current, streams.Token);
                }

                await Task.Delay(current.Count == 0 ? options.RetryDelay : options.UniverseRefreshInterval, clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The scanner stops.
        }
        finally
        {
            if (streams is not null)
            {
                await streams.CancelAsync();
                await Settle(running);
                streams.Dispose();
            }

            await Settle(snapshots);
        }
    }

    private static async Task Settle(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Cancelled with its streams.
        }
    }

    private static DateTimeOffset Floor(DateTimeOffset time, Timeframe timeframe)
    {
        return timeframe.FloorToGrid(time);
    }

    private async Task<HashSet<string>?> LoadUniverse(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ScanInstrumentInternal> universe = await source.Universe(cancellationToken);

            foreach (ScanInstrumentInternal instrument in universe)
            {
                _listedAt[instrument.Symbol] = instrument.ListedAt;
            }

            return [.. universe.Select(instrument => instrument.Symbol)];
        }
        catch (MarketScanException exception)
        {
            logger.LogWarning("The {Venue} scan universe could not be read ({Code}).", Venue, exception.Code);

            return null;
        }
    }

    /// <summary>
    ///     Starts one stream per batch of pairs; pairs that stay keep their candles, pairs that left are dropped.
    /// </summary>
    private Task Start(HashSet<string> universe, CancellationToken cancellationToken)
    {
        foreach (string symbol in _series.Keys.Where(symbol => !universe.Contains(symbol)))
        {
            _series.TryRemove(symbol, out _);
        }

        ScanSeries[][] batches =
        [
            .. universe.Order(StringComparer.Ordinal)
                .Select(symbol => _series.GetOrAdd(symbol, name => new ScanSeries(name)))
                .Chunk(source.MaxSymbolsPerStream)
        ];

        var firstPasses = new List<Task>();
        var streams = new List<Task>();

        foreach (ScanSeries[] batch in batches)
        {
            var firstPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            firstPasses.Add(firstPass.Task);
            streams.Add(RunStream(batch, firstPass, cancellationToken));
        }

        _ = Task.WhenAll(firstPasses).ContinueWith(_ => _ready = true, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

        return Task.WhenAll(streams);
    }

    private async Task RunStream(ScanSeries[] batch, TaskCompletionSource firstPass, CancellationToken cancellationToken)
    {
        TimeSpan delay = options.ReconnectDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (ScanSeries series in batch)
            {
                series.MarkGap();
            }

            DateTimeOffset connectedAt = clock.GetUtcNow();
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task reading = Read(batch, subscribed, connection.Token);
            Task repairing = Repair(batch, subscribed.Task, firstPass, connection.Token);

            await Task.WhenAny(reading, repairing);
            await connection.CancelAsync();

            try
            {
                await Task.WhenAll(reading, repairing);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    "A {Venue} scan stream of {Count} pairs disconnected with {ExceptionType}.",
                    Venue,
                    batch.Length,
                    exception.GetType().Name);
            }
            catch (OperationCanceledException)
            {
                // The stream stalled or the other task ended.
            }

            delay = clock.GetUtcNow() - connectedAt > options.MaxReconnectDelay
                ? options.ReconnectDelay
                : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxReconnectDelay.Ticks));
            await Task.Delay(delay, clock, cancellationToken);
        }
    }

    /// <summary>
    ///     Feeds the stream into the series; a stream silent for <see cref="ScannerOptions.StreamStallTimeout" /> is cancelled.
    /// </summary>
    private async Task Read(ScanSeries[] batch, TaskCompletionSource subscribed, CancellationToken cancellationToken)
    {
        Dictionary<string, ScanSeries> bySymbol = batch.ToDictionary(series => series.Symbol, StringComparer.Ordinal);
        InstrumentSymbol[] symbols = [.. batch.Select(series => new InstrumentSymbol(series.Symbol))];
        using var watchdog = new CancellationTokenSource(options.StreamStallTimeout, clock);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watchdog.Token);

        try
        {
            await foreach (ScanCandleUpdateInternal update in source.Stream(symbols, reading.Token))
            {
                watchdog.CancelAfter(options.StreamStallTimeout);
                subscribed.TrySetResult();

                if (!update.IsAlive && bySymbol.TryGetValue(update.Symbol, out ScanSeries? series))
                {
                    series.Accept(update);
                }
            }
        }
        finally
        {
            subscribed.TrySetResult();
        }
    }

    /// <summary>
    ///     Once the stream delivers (or after <see cref="ScannerOptions.SubscribeWait" />), reads the history of every pair that has
    ///     none and backfills every pair with a gap; then keeps trying the ones that failed.
    /// </summary>
    private async Task Repair(ScanSeries[] batch, Task subscribed, TaskCompletionSource firstPass, CancellationToken cancellationToken)
    {
        await Task.WhenAny(subscribed, Task.Delay(options.SubscribeWait, clock, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            foreach (ScanSeries series in batch.Where(series => !series.IsWarm || series.HasGap))
            {
                if (!await Fill(series, cancellationToken))
                {
                    break;
                }
            }

            firstPass.TrySetResult();
            await Task.Delay(options.RepairInterval, clock, cancellationToken);
        }
    }

    /// <summary>
    ///     Reads what the series misses over REST and merges it; false when the venue refused, so the pass stops for now.
    /// </summary>
    private async Task<bool> Fill(ScanSeries series, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset upToMinute = Floor(now, MinuteFrame);
        DateTimeOffset upToHour = Floor(now, HourFrame);
        var symbol = new InstrumentSymbol(series.Symbol);

        try
        {
            if (series.BackfillFrom is { } from && upToMinute - from.Minutes <= MinuteHistory)
            {
                IReadOnlyList<ScanCandle> minutes = from.Minutes < upToMinute
                    ? await source.Candles(symbol, MinuteFrame, from.Minutes, upToMinute, cancellationToken)
                    : [];
                IReadOnlyList<ScanCandle> hours = from.Hours < upToHour ? await source.Candles(symbol, HourFrame, from.Hours, upToHour, cancellationToken) : [];
                series.Merge(minutes, hours, listedAt: null);
            }
            else
            {
                DateTimeOffset minutesFrom = upToMinute - MinuteHistory;
                DateTimeOffset hoursFrom = upToHour - HourHistory;
                IReadOnlyList<ScanCandle> minutes = await source.Candles(symbol, MinuteFrame, minutesFrom, upToMinute, cancellationToken);
                IReadOnlyList<ScanCandle> hours = await source.Candles(symbol, HourFrame, hoursFrom, upToHour, cancellationToken);
                series.Merge(minutes, hours, ListedAt(series.Symbol, minutes, minutesFrom, hours, hoursFrom));
            }

            return true;
        }
        catch (MarketScanException exception)
        {
            logger.LogWarning("The {Venue} history of {Symbol} could not be read ({Code}).", Venue, series.Symbol, exception.Code);

            if (exception.Code == MarketScanErrorCodes.RequestsPaused)
            {
                await Task.Delay(options.RetryDelay, clock, cancellationToken);
            }

            return false;
        }
    }

    /// <summary>
    ///     The venue's listing time, or the first candle when the history starts later than asked, which only a pair listed within
    ///     the history's span does.
    /// </summary>
    private DateTimeOffset? ListedAt(
        string symbol,
        IReadOnlyList<ScanCandle> minutes,
        DateTimeOffset minutesFrom,
        IReadOnlyList<ScanCandle> hours,
        DateTimeOffset hoursFrom)
    {
        if (_listedAt.TryGetValue(symbol, out DateTimeOffset? known) && known is not null)
        {
            return known;
        }

        if (hours.Count > 0)
        {
            return hours[0].OpenTime > hoursFrom ? hours[0].OpenTime : null;
        }

        return minutes.Count > 0 && minutes[0].OpenTime > minutesFrom ? minutes[0].OpenTime : null;
    }

    private async Task RunSnapshots(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset now = clock.GetUtcNow();
            DateTimeOffset due = Floor(now, MinuteFrame) + MinuteFrame.Duration + options.SnapshotDelay;
            await Task.Delay(due - now, clock, cancellationToken);

            if (!_ready)
            {
                continue;
            }

            try
            {
                await Snapshot(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("The {Venue} scan snapshot failed with {ExceptionType}.", Venue, exception.GetType().Name);
            }
        }
    }

    private async Task Snapshot(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset upToMinute = Floor(now, MinuteFrame);
        Dictionary<string, ScanQuoteInternal> quotes = await Quotes(cancellationToken);
        var rows = new List<ScanSnapshotRowInternal>();

        foreach (ScanSeries series in _series.Values)
        {
            series.AdvanceTo(upToMinute);

            if (series.Input(now, quotes.GetValueOrDefault(series.Symbol)) is not { } input)
            {
                continue;
            }

            try
            {
                rows.Add(new ScanSnapshotRowInternal(series.Symbol, ScanMetricCalculator.Calculate(input), series.HasGap));
            }
            catch (ScanInputException exception)
            {
                logger.LogWarning("The {Venue} metrics of {Symbol} were not computed: {Reason}", Venue, series.Symbol, exception.Message);
            }
        }

        decimal? market = source.ReferenceSymbol is { } reference ? rows.FirstOrDefault(row => row.Symbol == reference)?.Metrics.TrendEma50 : null;
        IEnumerable<ScanSnapshotRowInternal> marked = rows.Select(row => row with { Metrics = row.Metrics with { MarketTrend = market } });
        using IServiceScope scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(
            new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(Venue, now, [.. marked.OrderBy(row => row.Symbol, StringComparer.Ordinal)])),
            cancellationToken);
    }

    private async Task<Dictionary<string, ScanQuoteInternal>> Quotes(CancellationToken cancellationToken)
    {
        try
        {
            return (await source.Quotes(cancellationToken)).ToDictionary(quote => quote.Symbol, StringComparer.Ordinal);
        }
        catch (MarketScanException exception)
        {
            logger.LogWarning("The {Venue} quote snapshot could not be read ({Code}); spreads stay empty.", Venue, exception.Code);

            return [];
        }
    }
}