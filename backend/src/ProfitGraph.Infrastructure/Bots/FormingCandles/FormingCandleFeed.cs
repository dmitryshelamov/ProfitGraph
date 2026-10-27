using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Infrastructure.Bots.FormingCandles;

/// <summary>
///     One upstream subscription of a series, shared by every client connection that follows it. Candles are passed on at
///     most once per throttle interval, except a closed candle or the first one of a new interval, which go at once; the
///     newest candle held back goes out on the next tick. A lost connection or a silent venue reports the feed without data;
///     it connects again with a doubling delay, and the next candle reports it live again.
/// </summary>
internal sealed class FormingCandleFeed(
    FormingFeedKeyInternal key,
    IFormingCandleSource source,
    ExchangeCredentialSecretsInternal? access,
    IFormingCandleSink sink,
    FormingCandleOptions options,
    TimeProvider clock,
    ILogger logger)
{
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private readonly SemaphoreSlim _publishing = new SemaphoreSlim(initialCount: 1, maxCount: 1);
    private readonly HashSet<string> _members = [];
    private readonly Lock _gate = new Lock();
    private FormingCandleInternal? _latest;
    private FormingCandleInternal? _pending;
    private FormingCandleInternal? _published;
    private DateTimeOffset _publishedAt;
    private DateTimeOffset _receivedAt;
    private FormingFeedStatus _status = FormingFeedStatus.NoData;

    public FormingFeedKeyInternal Key => key;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _members.Count;
            }
        }
    }

    public Task Completion { get; private set; } = Task.CompletedTask;

    public FormingFeedSnapshotInternal Add(string connectionId)
    {
        lock (_gate)
        {
            _members.Add(connectionId);

            return new FormingFeedSnapshotInternal(key, _status, _latest);
        }
    }

    public void Remove(string connectionId)
    {
        lock (_gate)
        {
            _members.Remove(connectionId);
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
        var symbol = new InstrumentSymbol(key.Symbol);
        Timeframe timeframe = Timeframe.Parse(key.Timeframe);
        var failures = 0;
        using ITimer ticks = clock.CreateTimer(_ => _ = Tick(), state: null, options.Throttle, options.Throttle);

        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                try
                {
                    using (MarketDataAccess.Use(access))
                    {
                        await foreach (FormingCandleInternal candle in source.Subscribe(symbol, timeframe, _stopping.Token))
                        {
                            failures = 0;
                            await Receive(candle);
                        }
                    }
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        "The forming candle feed of {Venue} {Symbol} {Timeframe} disconnected with {ExceptionType}.",
                        key.Venue,
                        key.Symbol,
                        key.Timeframe,
                        exception.GetType().Name);
                }

                await Lose();
                await Task.Delay(options.Backoff(failures++), clock, _stopping.Token);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The last client left.
        }
    }

    private async Task Receive(FormingCandleInternal candle)
    {
        await _publishing.WaitAsync(_stopping.Token);

        try
        {
            if (_published is { } published && candle.OpenTime < published.OpenTime)
            {
                return;
            }

            DateTimeOffset now = clock.GetUtcNow();
            _receivedAt = now;

            lock (_gate)
            {
                _latest = candle;
            }

            await Change(FormingFeedStatus.Live);

            if (candle.IsClosed || _published?.OpenTime != candle.OpenTime || now - _publishedAt >= options.Throttle)
            {
                await Publish(candle, now);
            }
            else
            {
                _pending = candle;
            }
        }
        finally
        {
            _publishing.Release();
        }
    }

    private async Task Tick()
    {
        try
        {
            await _publishing.WaitAsync(_stopping.Token);

            try
            {
                DateTimeOffset now = clock.GetUtcNow();

                if (_pending is { } pending)
                {
                    await Publish(pending, now);
                }

                if (_status == FormingFeedStatus.Live && now - _receivedAt > options.SilenceAfter)
                {
                    await Change(FormingFeedStatus.NoData);
                }
            }
            finally
            {
                _publishing.Release();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Stopped between ticks.
        }
        catch (Exception exception)
        {
            logger.LogWarning("A forming candle feed tick failed with {ExceptionType}.", exception.GetType().Name);
        }
    }

    private async Task Lose()
    {
        try
        {
            await _publishing.WaitAsync(_stopping.Token);

            try
            {
                await Change(FormingFeedStatus.NoData);
            }
            finally
            {
                _publishing.Release();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Stopped while reporting.
        }
    }

    private async Task Publish(FormingCandleInternal candle, DateTimeOffset now)
    {
        _pending = null;
        _published = candle;
        _publishedAt = now;

        try
        {
            await sink.Candle(key, candle, _stopping.Token);
        }
        catch (Exception exception) when (!_stopping.IsCancellationRequested)
        {
            logger.LogWarning("Delivering a forming candle failed with {ExceptionType}.", exception.GetType().Name);
        }
    }

    private async Task Change(FormingFeedStatus status)
    {
        if (_status == status)
        {
            return;
        }

        lock (_gate)
        {
            _status = status;
        }

        try
        {
            await sink.Status(key, status, _stopping.Token);
        }
        catch (Exception exception) when (!_stopping.IsCancellationRequested)
        {
            logger.LogWarning("Delivering a forming candle feed status failed with {ExceptionType}.", exception.GetType().Name);
        }
    }
}