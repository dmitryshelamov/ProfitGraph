using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     The engine of one leased bot, fed from one sequential queue (Profit Fox Designer ADR 0054). Every drained root is
///     committed by <see cref="BotRootCommitter" /> with the order intents it created; a fenced commit stops the session.
/// </summary>
public sealed class BotEngineSession
{
    private readonly BotId _botId;
    private readonly LeaseHolderId _holder;
    private readonly long _epoch;
    private readonly Timeframe _timeframe;
    private readonly CausalDispatcher _dispatcher;
    private readonly IOrderGateway _gateway;
    private readonly BotRootCommitter _committer;
    private readonly TimeProvider _clock;
    private readonly BotParameters _appliedParameters;
    private readonly StrategyVersionId _version;
    private Candle? _lastCandle;
    private int _lastCandleIndex;

    public GraphEngineCheckpoint? Checkpoint { get; private set; }

    internal BotEngineSession(
        BotId botId,
        LeaseHolderId holder,
        long epoch,
        Timeframe timeframe,
        CausalDispatcher dispatcher,
        IOrderGateway gateway,
        BotRootCommitter committer,
        TimeProvider clock,
        BotParameters appliedParameters,
        StrategyVersionId version,
        GraphEngineCheckpoint? checkpoint)
    {
        _botId = botId;
        _holder = holder;
        _epoch = epoch;
        _timeframe = timeframe;
        _dispatcher = dispatcher;
        _gateway = gateway;
        _committer = committer;
        _clock = clock;
        _appliedParameters = appliedParameters;
        _version = version;
        Checkpoint = checkpoint;
        _lastCandle = checkpoint?.LastClosedCandle;
        _lastCandleIndex = checkpoint?.LastClosedCandleIndex ?? -1;
    }

    /// <summary>
    ///     A first run warms indicators on the closed history and starts at its last candle, emitting <c>Start</c> once.
    ///     Without history it starts at the first live candle.
    /// </summary>
    internal async Task StartFromHistory(IReadOnlyList<Candle> closedHistory, CancellationToken cancellationToken)
    {
        if (Checkpoint is not null)
        {
            throw new InvalidOperationException("The bot already has a run.");
        }

        if (closedHistory.Count == 0)
        {
            return;
        }

        for (var index = 0; index < closedHistory.Count - 1; index++)
        {
            _dispatcher.AdvanceClosedCandle(closedHistory[index], index);
        }

        await StartAt(closedHistory[^1], closedHistory.Count - 1, cancellationToken);
    }

    /// <summary>
    ///     A restored run warms on the candles missed while it was stopped, without <c>CandleClosed</c> events, then drains one
    ///     <c>Resume(Restart)</c> at the latest closed candle. It never starts again.
    /// </summary>
    internal async Task ResumeAndWarm(IReadOnlyList<Candle> missed, CancellationToken cancellationToken)
    {
        foreach (Candle candle in missed.Where(candle => candle.OpenTime > _lastCandle!.OpenTime))
        {
            _lastCandleIndex++;
            _dispatcher.AdvanceClosedCandle(candle, _lastCandleIndex);
            _lastCandle = candle;
        }

        _dispatcher.ResumeRestartAndDrain(_lastCandle!.OpenTime + _timeframe.Duration, cancellationToken);
        await Save(liveCandle: false, cancellationToken);
    }

    /// <summary>
    ///     Processes one live closed candle. Candles arrive without gaps; an older or repeated one is ignored.
    /// </summary>
    public async Task ProcessCandle(Candle candle, CancellationToken cancellationToken)
    {
        if (Checkpoint is null)
        {
            await StartAt(candle, index: 0, cancellationToken);

            return;
        }

        if (candle.OpenTime <= _lastCandle!.OpenTime)
        {
            return;
        }

        if (candle.OpenTime != _lastCandle.OpenTime + _timeframe.Duration)
        {
            throw new InvalidOperationException("The live candle sequence has a gap.");
        }

        int index = checked(_lastCandleIndex + 1);
        DateTimeOffset closeTime = candle.OpenTime + _timeframe.Duration;
        _dispatcher.BeginOpen(new ReachedMarketContext(candle, index, closeTime, PricePhase.Open, candle.Open, index));
        _dispatcher.AdvanceClosedCandle(candle, index);
        _lastCandle = candle;
        _lastCandleIndex = index;
        var close = new ReachedMarketContext(candle, index, closeTime, PricePhase.Close, candle.Close, index + 1);
        _dispatcher.BeginClose(close);

        if (_dispatcher.AreIndicatorsReady)
        {
            _dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(candle), close);
            _dispatcher.Drain(cancellationToken);
        }

        await Save(liveCandle: true, cancellationToken);
    }

    /// <summary>
    ///     Applies one venue update against the last closed candle and drains the events it causes.
    /// </summary>
    public async Task ProcessExternalUpdate(ExternalOrderUpdate update, CancellationToken cancellationToken)
    {
        if (Checkpoint is null || _lastCandle is null)
        {
            return;
        }

        var context = new ReachedMarketContext(
            _lastCandle,
            _lastCandleIndex,
            _lastCandle.OpenTime + _timeframe.Duration,
            PricePhase.External,
            _lastCandle.Close,
            _lastCandleIndex + 1);
        _dispatcher.ApplyExternalOrderUpdate(update, context);
        _dispatcher.Drain(cancellationToken);
        await Save(liveCandle: false, cancellationToken);
    }

    private async Task StartAt(Candle candle, int index, CancellationToken cancellationToken)
    {
        _dispatcher.StartAndDrain(new ReachedMarketContext(candle, index, candle.OpenTime + _timeframe.Duration, PricePhase.Open, candle.Open, index));
        _dispatcher.AdvanceClosedCandle(candle, index);
        _lastCandle = candle;
        _lastCandleIndex = index;
        await Save(liveCandle: false, cancellationToken);
    }

    private async Task Save(bool liveCandle, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        GraphEngineCheckpoint current = _dispatcher.CaptureCheckpoint(_version.Value.ToString("D"), _lastCandle!.OpenTime + _timeframe.Duration);
        await _committer.Commit(
            new BotRootCommit(_botId, _holder, _epoch, current, _appliedParameters, NewIntents(Checkpoint, current, now)),
            bot =>
            {
                bot.ApplyStrategyVersion(_version, now);

                if (liveCandle)
                {
                    bot.ResetRestartStreak(now);
                }
            },
            cancellationToken);
        Checkpoint = current;
    }

    /// <summary>
    ///     A new pending order needs a submit; an order that turned pending-cancel needs a cancel.
    /// </summary>
    private List<OrderIntent> NewIntents(GraphEngineCheckpoint? before, GraphEngineCheckpoint after, DateTimeOffset now)
    {
        Dictionary<long, OrderSnapshot> previous = before?.Account.Orders.ToDictionary(order => order.Id) ?? [];
        var intents = new List<OrderIntent>();

        foreach (OrderSnapshot order in after.Account.Orders)
        {
            bool isNew = !previous.TryGetValue(order.Id, out OrderSnapshot? old);
            ClientOrderId clientOrderId = _gateway.ClientOrderIdFor(_botId, order.Id);

            if (isNew && order.Status is OrderStatus.PendingSubmit or OrderStatus.PendingCancel)
            {
                intents.Add(OrderIntent.Submit(OrderIntentId.New(), _botId, order, clientOrderId, now));
            }

            if (order.Status == OrderStatus.PendingCancel && (isNew || old!.Status != OrderStatus.PendingCancel))
            {
                intents.Add(OrderIntent.Cancel(OrderIntentId.New(), _botId, order.Id, clientOrderId, fromKill: false, now));
            }
        }

        return intents;
    }
}