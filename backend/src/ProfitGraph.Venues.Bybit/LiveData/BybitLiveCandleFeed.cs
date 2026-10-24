using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     One public kline subscription of a series, shared by every bot on it. Each confirmed candle newer than the last is
///     published once. A feed silent for its timeframe plus a margin reconnects, reports itself stale and fills the gap
///     from public history; the next candle reports it healthy again.
/// </summary>
internal sealed class BybitLiveCandleFeed(
    InstrumentSymbol symbol,
    Timeframe timeframe,
    TimeSpan stallAfter,
    IBybitPublicStreamTransport transport,
    BybitLiveHistory history,
    Func<bool, CancellationToken, Task> reportStale,
    TimeProvider clock,
    ILogger logger)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private readonly HashSet<ChannelWriter<Candle>> _subscribers = [];
    private DateTimeOffset? _lastOpen;
    private bool _stale;

    public int Count
    {
        get
        {
            lock (_subscribers)
            {
                return _subscribers.Count;
            }
        }
    }

    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Add(ChannelWriter<Candle> subscriber)
    {
        lock (_subscribers)
        {
            _subscribers.Add(subscriber);
        }
    }

    public void Remove(ChannelWriter<Candle> subscriber)
    {
        lock (_subscribers)
        {
            _subscribers.Remove(subscriber);
        }
    }

    public void Start()
    {
        Completion = Run();
    }

    public void Stop()
    {
        _stopping.Cancel();
    }

    private async Task Run()
    {
        string topic = BybitKlineMessages.Topic(symbol.Value, timeframe);

        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                using var watchdog = new CancellationTokenSource(stallAfter, clock);
                using var connected = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, watchdog.Token);

                try
                {
                    await foreach (string message in transport.Connect(topic, connected.Token))
                    {
                        foreach (Candle candle in BybitKlineMessages.Closed(message, topic, timeframe))
                        {
                            if (Publish(candle))
                            {
                                watchdog.CancelAfter(stallAfter);
                                await ReportStale(stale: false);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (watchdog.IsCancellationRequested)
                {
                    logger.LogWarning("The live kline feed of {Symbol} {Timeframe} stalled.", symbol.Value, timeframe.Key);
                    await ReportStale(stale: true);
                    await Backfill();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        "The live kline feed of {Symbol} {Timeframe} disconnected with {ExceptionType}.",
                        symbol.Value,
                        timeframe.Key,
                        exception.GetType().Name);
                }

                await Task.Delay(ReconnectDelay, clock, _stopping.Token);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The last subscriber left.
        }
        finally
        {
            lock (_subscribers)
            {
                foreach (ChannelWriter<Candle> subscriber in _subscribers)
                {
                    subscriber.TryComplete();
                }
            }

            _stopping.Dispose();
        }
    }

    private bool Publish(Candle candle)
    {
        lock (_subscribers)
        {
            if (_lastOpen is { } last && candle.OpenTime <= last)
            {
                return false;
            }

            _lastOpen = candle.OpenTime;

            foreach (ChannelWriter<Candle> subscriber in _subscribers)
            {
                subscriber.TryWrite(candle);
            }

            return true;
        }
    }

    private async Task Backfill()
    {
        if (_lastOpen is not { } last)
        {
            return;
        }

        try
        {
            DateTimeOffset to = timeframe.FloorToGrid(clock.GetUtcNow());

            foreach (Candle candle in await history.After(symbol, timeframe, last, to, _stopping.Token))
            {
                if (Publish(candle))
                {
                    await ReportStale(stale: false);
                }
            }
        }
        catch (Exception exception) when (!_stopping.IsCancellationRequested)
        {
            logger.LogWarning(
                "Backfilling the live kline feed of {Symbol} {Timeframe} failed with {ExceptionType}.",
                symbol.Value,
                timeframe.Key,
                exception.GetType().Name);
        }
    }

    private async Task ReportStale(bool stale)
    {
        if (_stale == stale)
        {
            return;
        }

        try
        {
            await reportStale(stale, _stopping.Token);
            _stale = stale;
        }
        catch (Exception exception) when (!_stopping.IsCancellationRequested)
        {
            logger.LogWarning(
                "Reporting the live kline feed of {Symbol} {Timeframe} failed with {ExceptionType}.",
                symbol.Value,
                timeframe.Key,
                exception.GetType().Name);
        }
    }
}