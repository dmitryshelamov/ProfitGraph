using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
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
    private readonly Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<HistoricalCandlesInternal>>? _gaps;
    private StrategyVersionId _version;
    private int _lastCandleIndex;
    private (Candle Previous, Candle Restated)? _restatement;
    private IReadOnlyList<StrategyParameter>? _switchedParameters;

    public GraphEngineCheckpoint? Checkpoint { get; private set; }

    /// <summary>
    ///     The strategy version the engine runs now.
    /// </summary>
    public StrategyVersionId Version => _version;

    /// <summary>
    ///     The parameter values the run applies to every version it switches to.
    /// </summary>
    public BotParameters AppliedParameters { get; private set; }

    /// <summary>
    ///     The last closed candle the engine processed, or null before the first.
    /// </summary>
    public Candle? LastCandle { get; private set; }

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
        GraphEngineCheckpoint? checkpoint,
        Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<HistoricalCandlesInternal>>? gaps = null)
    {
        _botId = botId;
        _holder = holder;
        _epoch = epoch;
        _timeframe = timeframe;
        _dispatcher = dispatcher;
        _gateway = gateway;
        _committer = committer;
        _clock = clock;
        _gaps = gaps;
        AppliedParameters = appliedParameters;
        _version = version;
        Checkpoint = checkpoint;
        LastCandle = checkpoint?.LastClosedCandle;
        _lastCandleIndex = checkpoint?.LastClosedCandleIndex ?? -1;
    }

    /// <summary>
    ///     The venue restated the checkpoint's last candle (ADR 0029): the first commit of the session records it in the bot's feed.
    /// </summary>
    internal void NoteRestatedCandle(Candle previous, Candle restated)
    {
        _restatement = (previous, restated);
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
        foreach (Candle candle in missed.Where(candle => candle.OpenTime > LastCandle!.OpenTime))
        {
            _lastCandleIndex++;
            _dispatcher.AdvanceClosedCandle(candle, _lastCandleIndex);
            LastCandle = candle;
        }

        _dispatcher.ResumeRestartAndDrain(LastCandle!.OpenTime + _timeframe.Duration, cancellationToken);
        await Save(liveCandle: false, cancellationToken);
    }

    /// <summary>
    ///     After a start's reconciliation changed the engine's orders (a cancel, a rejection or a closed order the venue no longer
    ///     knows), one more <c>Resume(Restart)</c> is drained at the latest closed candle, so the strategy restores what the
    ///     reconciliation took away: the resume at open ran before the venue's answer and saw those orders as working (ADR 0035).
    /// </summary>
    public async Task ResumeAfterReconciliation(CancellationToken cancellationToken)
    {
        if (Checkpoint is null || LastCandle is null)
        {
            return;
        }

        _dispatcher.ResumeRestartAndDrain(LastCandle.OpenTime + _timeframe.Duration, cancellationToken);
        await Save(liveCandle: false, cancellationToken);
    }

    /// <summary>
    ///     Processes one live closed candle; an older or repeated one is ignored. Before a candle after a gap the venue's
    ///     history of the gap is read: its candles are processed in order and its market closures are recorded (ADR 0023,
    ///     TC3). A gap that neither covers fails.
    /// </summary>
    public async Task ProcessCandle(Candle candle, CancellationToken cancellationToken)
    {
        if (Checkpoint is null)
        {
            await StartAt(candle, index: 0, cancellationToken);

            return;
        }

        if (candle.OpenTime <= LastCandle!.OpenTime)
        {
            return;
        }

        if (candle.OpenTime != LastCandle.OpenTime + _timeframe.Duration)
        {
            await FillGap(candle.OpenTime, cancellationToken);
        }

        await ProcessNext(candle, cancellationToken);
    }

    private async Task FillGap(DateTimeOffset until, CancellationToken cancellationToken)
    {
        DateTimeOffset cursor = LastCandle!.OpenTime + _timeframe.Duration;
        HistoricalCandlesInternal history = _gaps is null
            ? HistoricalCandlesInternal.Contiguous([])
            : await _gaps(cursor, until, cancellationToken);
        Dictionary<DateTimeOffset, Candle> candles = history.Candles.Where(candle => candle.OpenTime >= cursor && candle.OpenTime < until)
            .ToDictionary(candle => candle.OpenTime);

        while (cursor < until)
        {
            if (candles.TryGetValue(cursor, out Candle? missed))
            {
                await ProcessNext(missed, cancellationToken);
                cursor += _timeframe.Duration;

                continue;
            }

            CandleRangeInternal? closure = history.Closures.FirstOrDefault(range => range.From <= cursor && cursor < range.To);

            if (closure is null)
            {
                throw new InvalidOperationException("The live candle sequence has a gap.");
            }

            DateTimeOffset end = closure.To < until ? closure.To : until;
            _dispatcher.RecordClosure(new MarketClosure(cursor, end));
            cursor = end;
        }
    }

    private async Task ProcessNext(Candle candle, CancellationToken cancellationToken)
    {
        int index = checked(_lastCandleIndex + 1);
        DateTimeOffset closeTime = candle.OpenTime + _timeframe.Duration;
        _dispatcher.BeginOpen(new ReachedMarketContext(candle, index, closeTime, PricePhase.Open, candle.Open, index));
        _dispatcher.AdvanceClosedCandle(candle, index);
        LastCandle = candle;
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
    public async Task ProcessExternalUpdate(ExternalOrderUpdate update, CancellationToken cancellationToken, bool resumeAfterVenueCancel = true)
    {
        if (Checkpoint is null || LastCandle is null)
        {
            return;
        }

        var context = new ReachedMarketContext(
            LastCandle,
            _lastCandleIndex,
            LastCandle.OpenTime + _timeframe.Duration,
            PricePhase.External,
            LastCandle.Close,
            _lastCandleIndex + 1);

        // The venue reports fills as they happen, so the position opens at the candle the fill falls in.
        IReadOnlyList<EventOccurrence> occurrences = _dispatcher.ApplyExternalOrderUpdate(
            update,
            context,
            update.Kind is ExternalOrderUpdateKind.PartialFill or ExternalOrderUpdateKind.FinalFill ? _clock.GetUtcNow() : null);
        _dispatcher.Drain(cancellationToken);

        // An order the venue cancelled without the engine asking (the owner's cancel at the broker) leaves the strategy's grid short of
        // an order: one more resume lets the strategy restore it (ADR 0039). The engine's own cancels also complete a pending cancel.
        if (resumeAfterVenueCancel &&
            occurrences.Any(occurrence => occurrence.Kind == EventKind.Cancelled) &&
            occurrences.All(occurrence => occurrence.Kind != EventKind.CancelCompleted))
        {
            _dispatcher.ResumeRestartAndDrain(LastCandle.OpenTime + _timeframe.Duration, cancellationToken);
        }

        await Save(liveCandle: false, cancellationToken, update: update);
    }

    /// <summary>
    ///     Switches the engine to <paramref name="candidate" /> between roots (ADR 0019): indicators whose definition is
    ///     unchanged keep their state, the others warm on <paramref name="closedHistory" />, orders of removed blocks are
    ///     cancelled and one <c>Resume(GraphChanged)</c> is drained; the root is committed. <paramref name="applied" /> are the
    ///     run's values fitted to the candidate's <paramref name="parameters" />, and the bot's values follow them in the same
    ///     commit (ADR 0039). Returns null when it switched, or the reason the engine refused the candidate, in which case
    ///     nothing changed.
    /// </summary>
    internal async Task<string?> ChangeGraph(
        StrategyVersionId candidate,
        BotParameters applied,
        IReadOnlyList<StrategyParameter> parameters,
        LoweredStrategyGraph lowered,
        IReadOnlyList<Candle> closedHistory,
        CancellationToken cancellationToken)
    {
        GraphChangeResult result = _dispatcher.ChangeGraph(
            lowered.Graph,
            lowered.Catalog,
            EngineExecutionInputs.Empty,
            candidate.Value.ToString("D"),
            closedHistory,
            LastCandle!.OpenTime + _timeframe.Duration);

        if (!result.Applied)
        {
            return result.Diagnostic ?? "The engine refused the graph change.";
        }

        await Switched(candidate, applied, parameters, cancellationToken);

        return null;
    }

    /// <summary>
    ///     Gives a stored run the bot's current values and allocation (ADR 0025) on <paramref name="target" />, the strategy's
    ///     latest version, in one root (ADR 0039): the engine switches to it compiled with <paramref name="applied" />, the
    ///     account's capital becomes <paramref name="allocationCap" /> (the difference moves the quote balance), and one
    ///     <c>Resume(GraphChanged)</c> is drained; the root is committed. Returns null when it applied, or the reason it was
    ///     refused, in which case nothing changed.
    /// </summary>
    internal async Task<string?> ApplySettings(
        StrategyVersionId target,
        BotParameters applied,
        IReadOnlyList<StrategyParameter> parameters,
        decimal allocationCap,
        LoweredStrategyGraph lowered,
        IReadOnlyList<Candle> closedHistory,
        CancellationToken cancellationToken)
    {
        if (_dispatcher.Account is not ExternallyExecutedAccount account || !account.CanAdjustCapital(allocationCap))
        {
            return "The allocation cannot be below what the run has reserved.";
        }

        GraphChangeResult result = _dispatcher.ChangeGraph(
            lowered.Graph,
            lowered.Catalog,
            EngineExecutionInputs.Empty,
            target.Value.ToString("D"),
            closedHistory,
            LastCandle!.OpenTime + _timeframe.Duration);

        if (!result.Applied)
        {
            return result.Diagnostic ?? "The engine refused the new values.";
        }

        account.AdjustCapital(allocationCap);
        await Switched(target, applied, parameters, cancellationToken);

        return null;
    }

    /// <summary>
    ///     Submits an owner order (market or limit) against the last closed candle (Profit Fox Designer ADR 0091) and commits its
    ///     resolution with the root: the engine order it became, which then follows the normal intent path, or the engine's
    ///     rejection. Returns false when the engine has no candle yet and the request was left alone.
    /// </summary>
    public async Task<bool> ProcessManualOrder(ManualOrderRequest request, CancellationToken cancellationToken)
    {
        if (Checkpoint is null || LastCandle is not { } last)
        {
            return false;
        }

        var context = new ReachedMarketContext(last, _lastCandleIndex, last.OpenTime + _timeframe.Duration, PricePhase.External, last.Close, _lastCandleIndex + 1);
        OrderSubmission submission = _dispatcher.SubmitManualOrder(request.Side, request.Amount, request.LimitPrice, context);
        _dispatcher.Drain(cancellationToken);
        await Save(liveCandle: false, cancellationToken, new BotRootManualOrder(request.Id, submission.Order?.Id, submission.Rejection?.Reason.ToString()));

        return true;
    }

    private async Task StartAt(Candle candle, int index, CancellationToken cancellationToken)
    {
        _dispatcher.StartAndDrain(new ReachedMarketContext(candle, index, candle.OpenTime + _timeframe.Duration, PricePhase.Open, candle.Open, index));
        _dispatcher.AdvanceClosedCandle(candle, index);
        LastCandle = candle;
        _lastCandleIndex = index;
        await Save(liveCandle: false, cancellationToken);
    }

    /// <summary>
    ///     Commits the root of a version switch: the bot records the version and fits its values to the version's parameters.
    /// </summary>
    private async Task Switched(StrategyVersionId version, BotParameters applied, IReadOnlyList<StrategyParameter> parameters, CancellationToken cancellationToken)
    {
        _version = version;
        AppliedParameters = applied;
        _switchedParameters = parameters;

        try
        {
            await Save(liveCandle: false, cancellationToken);
        }
        finally
        {
            _switchedParameters = null;
        }
    }

    private async Task Save(bool liveCandle, CancellationToken cancellationToken, BotRootManualOrder? manualOrder = null, ExternalOrderUpdate? update = null)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        GraphEngineCheckpoint current = _dispatcher.CaptureCheckpoint(_version.Value.ToString("D"), LastCandle!.OpenTime + _timeframe.Duration);
        await _committer.Commit(
            new BotRootCommit(
                _botId,
                _holder,
                _epoch,
                current,
                AppliedParameters,
                NewIntents(Checkpoint, current, now),
                manualOrder,
                BotRootAccounting.Fills(_botId, Checkpoint, current, update, _dispatcher.Account.Rules.BuyFeeAsset, now),
                BotRootAccounting.Position(Checkpoint, current, now),
                BotRootAccounting.FeeCorrections(_botId, Checkpoint, current, update, now)),
            bot =>
            {
                bot.ApplyStrategyVersion(_version, now);

                if (_switchedParameters is { } parameters)
                {
                    bot.FitParameters(parameters, now);
                }

                if (_restatement is { } restatement)
                {
                    bot.RecordHistoryRestated(restatement.Previous.OpenTime, restatement.Previous.Close, restatement.Restated.Close, now);
                }

                if (liveCandle)
                {
                    bot.ResetRestartStreak(now);
                    DateTimeOffset closedAt = LastCandle!.OpenTime + _timeframe.Duration;
                    bot.RecordDayEquity(DateOnly.FromDateTime(closedAt.UtcDateTime), current.Account.QuoteTotal + current.Account.BaseTotal * LastCandle!.Close, now);
                }
            },
            cancellationToken);
        Checkpoint = current;
        _restatement = null;
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