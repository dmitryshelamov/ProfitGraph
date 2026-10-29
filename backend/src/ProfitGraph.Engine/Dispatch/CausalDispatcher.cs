using System.Collections.Immutable;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Per-run FIFO causal dispatch; candle matching and full replay are driven separately by the future engine.
/// </summary>
public sealed class CausalDispatcher
{
    private readonly EventRouter _router = new EventRouter();
    private readonly ActionCollector _collector = new ActionCollector();
    private readonly ActionExecutor _executor = new ActionExecutor();
    private readonly List<int> _routedSlots = new List<int>();
    private readonly Queue<(EventOccurrence Occurrence, ReachedMarketContext Market)> _queue = new Queue<(EventOccurrence Occurrence, ReachedMarketContext Market)>();
    private readonly HashSet<(long OccurrenceId, string NodeId)> _applied = new HashSet<(long OccurrenceId, string NodeId)>();
    private readonly CascadeGuard _guard = new CascadeGuard();
    private readonly List<DispatchObservation> _observations = new List<DispatchObservation>();
    private readonly List<EventOccurrence> _occurrences = new List<EventOccurrence>();
    private readonly List<ActionSkipDiagnostic> _skips = new List<ActionSkipDiagnostic>();
    private readonly List<ActionRequest> _requestsScratch = new List<ActionRequest>();
    private readonly List<ActionSkipDiagnostic> _skipsScratch = new List<ActionSkipDiagnostic>();
    private readonly Dictionary<long, long> _pendingSubmissionAttempts = new Dictionary<long, long>();

    private readonly Dictionary<long, (string NodeId, EventOccurrence Cause)> _pendingCancellations =
        new Dictionary<long, (string NodeId, EventOccurrence Cause)>();

    private readonly List<ExternalOrderDiagnostic> _externalDiagnostics = new List<ExternalOrderDiagnostic>();

    private readonly List<AppliedActionFact> _facts = new List<AppliedActionFact>();
    private readonly ReplayDispatchBatch _replayBatch;
    private readonly Dictionary<long, string> _orderGraphVersions = new Dictionary<long, string>();
    private readonly List<MarketClosure> _closures;
    private readonly SortedDictionary<string, GridState> _grids = new SortedDictionary<string, GridState>(StringComparer.Ordinal);
    private CompiledTradingGraph _graph;
    private EngineExecutionInputs _inputs;
    private CandleValueEvaluator _evaluator;
    private EngineValue[] _routedValues;
    private long _nextEventId;
    private long _nextAttemptId;
    private bool _faulted;
    private bool _isDraining;
    private int _lastClosedCandleIndex = -1;
    private Candle? _lastClosedCandle;
    private DateTimeOffset? _positionOpenedAt;

    public bool AreIndicatorsReady => _evaluator.AreIndicatorsReady;
    public IGraphAccount Account { get; }

    public DispatchBatch LastBatch => new DispatchBatch(_observations, _skips, _facts, _occurrences);
    public ExecutionPublicationFact? LastPublishedExecution { get; private set; }
    public IReadOnlyList<ExternalOrderDiagnostic> ExternalOrderDiagnostics => _externalDiagnostics.AsReadOnly();
    public bool Started { get; private set; }

    public string? GraphVersionId { get; private set; }

    public IReadOnlyDictionary<long, string> OrderGraphVersions
    {
        get
        {
            if (GraphVersionId is not null)
            {
                RememberOrderVersions();
            }

            return _orderGraphVersions.ToImmutableDictionary();
        }
    }

    public CausalDispatcher(
        CompiledTradingGraph graph,
        IGraphAccount account,
        EngineExecutionInputs? inputs = null,
        string? graphVersionId = null,
        IReadOnlyList<MarketClosure>? closures = null)
    {
        if (graphVersionId is not null && string.IsNullOrWhiteSpace(graphVersionId))
        {
            throw new ArgumentException("Graph version id must be nonempty.", nameof(graphVersionId));
        }

        _graph = graph;
        Account = account;
        _closures = [.. closures ?? []];
        _inputs = inputs ?? EngineExecutionInputs.Empty;
        GraphVersionId = graphVersionId;
        _replayBatch = new ReplayDispatchBatch(_facts, _occurrences);
        _evaluator = new CandleValueEvaluator(graph, _inputs);
        _routedValues = new EngineValue[graph.SlotCount];

        foreach (CompiledTradingNode node in graph.Nodes)
        {
            foreach (KeyValuePair<int, ValueType> binding in node.OutputSlots)
            {
                _routedValues[binding.Key] = EngineValue.Unavailable(binding.Value);
            }
        }
    }

    public void BeginOpen(ReachedMarketContext context)
    {
        EnsureIdle();

        if (context.Phase != PricePhase.Open || context.MarketPrice != context.Candle.Open)
        {
            throw new ArgumentException("Open root must use the actual reached candle Open.", nameof(context));
        }

        _guard.BeginOpen(context.CandleIndex, context.MarketPrice);
    }

    internal void BeginReplayOpen(int candleIndex, decimal marketPrice)
    {
        EnsureIdle();
        _guard.BeginOpen(candleIndex, marketPrice);
    }

    public void BeginClose(ReachedMarketContext context)
    {
        EnsureIdle();

        if (context.Phase != PricePhase.Close || context.MarketPrice != context.Candle.Close)
        {
            throw new ArgumentException("Close root must use the actual reached candle Close.", nameof(context));
        }

        _guard.BeginClose(context.CandleIndex, context.MarketPrice);
    }

    public void AdvancePrice(decimal marketPrice)
    {
        EnsureIdle();
        _guard.AdvancePrice(marketPrice);
    }

    public IReadOnlyDictionary<PortReference, EngineValue> AdvanceClosedCandle(Candle candle, int historyIndex)
    {
        EnsureIdle();
        _lastClosedCandle = candle;
        _lastClosedCandleIndex = historyIndex;

        return _evaluator.AdvanceClosedCandle(candle, historyIndex);
    }

    internal void CommitClosedCandle(Candle candle, int historyIndex)
    {
        EnsureIdle();
        _lastClosedCandle = candle;
        _lastClosedCandleIndex = historyIndex;
        _evaluator.CommitClosedCandle(candle, historyIndex);
    }

    /// <summary>
    ///     Starts an externally executed graph once, after its first replay-style Open root has drained.
    /// </summary>
    public DispatchBatch StartAndDrain(ReachedMarketContext context)
    {
        EnsureIdle();

        if (Started)
        {
            return LastBatch;
        }

        BeginOpen(context);
        EnqueueExternal(EventKind.Start, new StartPayload(), context);
        DispatchBatch batch = Drain();
        Started = true;

        return batch;
    }

    public GraphEngineCheckpoint CaptureCheckpoint(string graphVersionId, DateTimeOffset lastCandleCloseTime)
    {
        EnsureIdle();

        if (_isDraining || Account is not ExternallyExecutedAccount account || _lastClosedCandle is null ||
            _lastClosedCandleIndex < 0 || string.IsNullOrWhiteSpace(graphVersionId) ||
            lastCandleCloseTime.Offset != TimeSpan.Zero || lastCandleCloseTime <= _lastClosedCandle.OpenTime)
        {
            throw new InvalidOperationException("Checkpoint requires an idle external engine and its last closed candle.");
        }

        if (GraphVersionId is not null && GraphVersionId != graphVersionId)
        {
            throw new ArgumentException("Graph version differs from the running engine.", nameof(graphVersionId));
        }

        GraphVersionId = graphVersionId;
        RememberOrderVersions();

        return new GraphEngineCheckpoint(
            account.CaptureCheckpoint(),
            _nextEventId,
            _nextAttemptId,
            _pendingSubmissionAttempts.ToImmutableDictionary(),
            _pendingCancellations.Select(pair =>
                    new PendingCancellationCheckpoint(pair.Key, pair.Value.NodeId, EventCauseCheckpoint.Capture(pair.Value.Cause)))
                .ToImmutableArray(),
            _lastClosedCandle,
            _lastClosedCandleIndex,
            lastCandleCloseTime,
            Started,
            graphVersionId,
            _orderGraphVersions.ToImmutableDictionary(),
            _positionOpenedAt,
            _grids.Values.Where(grid => grid.Phase != GridPhase.Stopped).Select(grid => grid.Capture()).ToImmutableArray());
    }

    public static CausalDispatcher Restore(
        GraphEngineCheckpoint checkpoint,
        CompiledTradingGraph graph,
        ExecutionSettings settings,
        InstrumentRules rules,
        IReadOnlyList<Candle> closedHistory,
        EngineExecutionInputs? inputs = null,
        IReadOnlyList<MarketClosure>? closures = null)
    {
        return RestoreCore(checkpoint, graph, settings, rules, closedHistory, inputs, enqueueRestart: true, closures);
    }

    public static CausalDispatcher RestoreWithoutResume(
        GraphEngineCheckpoint checkpoint,
        CompiledTradingGraph graph,
        ExecutionSettings settings,
        InstrumentRules rules,
        IReadOnlyList<Candle> closedHistory,
        EngineExecutionInputs? inputs = null,
        IReadOnlyList<MarketClosure>? closures = null)
    {
        return RestoreCore(checkpoint, graph, settings, rules, closedHistory, inputs, enqueueRestart: false, closures);
    }

    private static CausalDispatcher RestoreCore(
        GraphEngineCheckpoint checkpoint,
        CompiledTradingGraph graph,
        ExecutionSettings settings,
        InstrumentRules rules,
        IReadOnlyList<Candle> closedHistory,
        EngineExecutionInputs? inputs,
        bool enqueueRestart,
        IReadOnlyList<MarketClosure>? closures)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(closedHistory);

        if (closedHistory.Count == 0 || closedHistory[^1] != checkpoint.LastClosedCandle ||
            checkpoint.LastClosedCandleIndex < closedHistory.Count - 1)
        {
            throw new ArgumentException("The supplied history must end at the checkpoint's last closed candle.", nameof(closedHistory));
        }

        ExternallyExecutedAccount account = ExternallyExecutedAccount.Restore(checkpoint.Account, settings, rules);
        var dispatcher = new CausalDispatcher(graph, account, inputs, closures: closures)
        {
            _nextEventId = checkpoint.NextEventId,
            _nextAttemptId = checkpoint.NextAttemptId,
            _lastClosedCandle = checkpoint.LastClosedCandle,
            _lastClosedCandleIndex = checkpoint.LastClosedCandleIndex,
            Started = checkpoint.Started,
            GraphVersionId = checkpoint.GraphVersionId,
            _positionOpenedAt = checkpoint.PositionOpenedAt
        };

        for (var index = 0; index < closedHistory.Count; index++)
        {
            dispatcher._evaluator.CommitClosedCandle(closedHistory[index], index);
        }

        dispatcher._evaluator.RelabelRestoredCandleIndex(checkpoint.LastClosedCandleIndex);

        foreach ((long orderId, long attemptId) in checkpoint.PendingSubmissionAttempts)
        {
            dispatcher._pendingSubmissionAttempts.Add(orderId, attemptId);
        }

        foreach (PendingCancellationCheckpoint pending in checkpoint.PendingCancellations)
        {
            dispatcher._pendingCancellations.Add(pending.OrderId, (pending.CancelNodeId, pending.Cause.Restore()));
        }

        foreach ((long orderId, string versionId) in checkpoint.OrderGraphVersions)
        {
            dispatcher._orderGraphVersions.Add(orderId, versionId);
        }

        foreach (GridCheckpoint grid in checkpoint.Grids)
        {
            dispatcher._grids.Add(grid.NodeId, GridState.Restore(grid));
        }

        if (enqueueRestart)
        {
            dispatcher.EnqueueResume(ResumeReason.Restart, checkpoint.GraphVersionId, checkpoint.GraphVersionId, checkpoint.LastCandleCloseTime);
        }

        return dispatcher;
    }

    public DispatchBatch ResumeRestartAndDrain(DateTimeOffset lastCandleCloseTime, CancellationToken cancellationToken = default)
    {
        EnsureIdle();

        if (Account is not ExternallyExecutedAccount || !Started || GraphVersionId is null ||
            lastCandleCloseTime.Offset != TimeSpan.Zero || _lastClosedCandle is null || lastCandleCloseTime <= _lastClosedCandle.OpenTime)
        {
            throw new InvalidOperationException("Restart resume requires a started external engine and its last closed candle.");
        }

        EnqueueResume(ResumeReason.Restart, GraphVersionId, GraphVersionId, lastCandleCloseTime);

        return Drain(cancellationToken);
    }

    private EventOccurrence EnqueueResume(ResumeReason reason, string previousVersionId, string currentVersionId, DateTimeOffset closeTime)
    {
        Candle candle = _lastClosedCandle ?? throw new InvalidOperationException("Resume requires a closed candle.");
        var context = new ReachedMarketContext(
            candle,
            _lastClosedCandleIndex,
            closeTime,
            PricePhase.External,
            candle.Close,
            checked(_lastClosedCandleIndex + 1));
        _guard.BeginExternal(context.CandleIndex, context.MarketPrice);

        return Enqueue(EventKind.Resume, new ResumePayload(reason, previousVersionId, currentVersionId), context, cause: null);
    }

    private void RememberOrderVersions()
    {
        foreach (OrderSnapshot order in Account.Capture().Orders)
        {
            _orderGraphVersions.TryAdd(order.Id, GraphVersionId!);
        }
    }

    public GraphChangeResult ChangeGraph(
        StrategyGraph candidate,
        IReadOnlyList<INodeDefinition> definitions,
        EngineExecutionInputs inputs,
        string newGraphVersionId,
        IReadOnlyList<Candle> closedHistory,
        DateTimeOffset lastCandleCloseTime)
    {
        if (_faulted || _isDraining || _queue.Count != 0)
        {
            return GraphChangeResultFailure("Causal work must drain before a graph change.");
        }

        if (GraphVersionId is null || string.IsNullOrWhiteSpace(newGraphVersionId) || _lastClosedCandle is null ||
            closedHistory.Count == 0 || closedHistory[^1] != _lastClosedCandle ||
            lastCandleCloseTime.Offset != TimeSpan.Zero || lastCandleCloseTime <= _lastClosedCandle.OpenTime)
        {
            return GraphChangeResultFailure("Graph version or closed-candle history is invalid.");
        }

        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(candidate, definitions);

        if (!compilation.Validation.IsValid || compilation.Graph is null)
        {
            return GraphChangeResultFailure(string.Join("; ", compilation.Validation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }

        CompiledTradingGraph nextGraph = compilation.Graph;

        // The account was opened for one direction; a running bot keeps it (ADR 0061).
        if (nextGraph.Direction != Account.Settings.Direction)
        {
            return GraphChangeResultFailure("A graph change cannot turn a long run into a short one or back.");
        }

        CandleValueEvaluator nextEvaluator;

        try
        {
            nextEvaluator = new CandleValueEvaluator(nextGraph, inputs);

            for (var index = 0; index < closedHistory.Count; index++)
            {
                nextEvaluator.CommitClosedCandle(closedHistory[index], index);
            }

            nextEvaluator.RelabelRestoredCandleIndex(_lastClosedCandleIndex);
        }
        catch (ArgumentException exception)
        {
            return GraphChangeResultFailure(exception.Message);
        }

        string[] kept = nextGraph.IndicatorNodes
            .Where(node => _graph.NodesById.TryGetValue(node.Node.Id, out CompiledTradingNode? old) && SameIndicator(old, node))
            .Select(node => node.Node.Id)
            .ToArray();

        foreach (string nodeId in kept)
        {
            nextEvaluator.KeepIndicatorState(_evaluator, nodeId);
        }

        RememberOrderVersions();
        string previousVersionId = GraphVersionId;
        _graph = nextGraph;
        _inputs = inputs;
        _evaluator = nextEvaluator;
        _routedValues = new EngineValue[nextGraph.SlotCount];
        _routedSlots.Clear();

        foreach (CompiledTradingNode node in nextGraph.Nodes)
        {
            foreach (KeyValuePair<int, ValueType> binding in node.OutputSlots)
            {
                _routedValues[binding.Key] = EngineValue.Unavailable(binding.Value);
            }
        }

        GraphVersionId = newGraphVersionId;
        var orphanIds = new List<long>();
        var immediate = new List<OrderSnapshot>();

        foreach (OrderSnapshot order in Account.Capture().Orders)
        {
            if (nextGraph.NodesById.ContainsKey(order.OriginNodeId) || order.OriginNodeId == ManualOrder.OriginNodeId)
            {
                continue;
            }

            orphanIds.Add(order.Id);

            if (Account is IExternallyExecutedAccount external)
            {
                external.RequestCancel(order.Id);
            }
            else if (Account.Cancel(order.Id) is { } cancelled)
            {
                immediate.Add(OrderSnapshot.Capture(cancelled));
            }
        }

        foreach ((long orderId, (string nodeId, _)) in _pendingCancellations.ToArray())
        {
            if (!nextGraph.NodesById.ContainsKey(nodeId))
            {
                _pendingCancellations.Remove(orderId);
                _externalDiagnostics.Add(new ExternalOrderDiagnostic(orderId, "CancelNodeRemoved"));
            }
        }

        var emitted = new List<EventOccurrence>();
        var observations = new List<DispatchObservation>();

        if (immediate.Count > 0)
        {
            ReachedMarketContext context = LastExternalContext(lastCandleCloseTime);
            _guard.BeginExternal(context.CandleIndex, context.MarketPrice);

            foreach (OrderSnapshot order in immediate)
            {
                _guard.Consume(order.OriginNodeId, causationId: null);
                emitted.Add(Enqueue(EventKind.Cancelled, new OrderPayload(order), context, cause: null));
            }

            observations.AddRange(Drain().Observations);
        }

        emitted.Add(EnqueueResume(ResumeReason.GraphChanged, previousVersionId, newGraphVersionId, lastCandleCloseTime));
        observations.AddRange(Drain().Observations);

        return new GraphChangeResult(
            Applied: true,
            Diagnostic: null,
            orphanIds.ToImmutableArray(),
            kept.ToImmutableArray(),
            AreIndicatorsReady,
            emitted.ToImmutableArray(),
            observations.ToImmutableArray());
    }

    private GraphChangeResult GraphChangeResultFailure(string diagnostic)
    {
        return new GraphChangeResult(
            Applied: false,
            diagnostic,
            ImmutableArray<long>.Empty,
            ImmutableArray<string>.Empty,
            AreIndicatorsReady,
            ImmutableArray<EventOccurrence>.Empty,
            ImmutableArray<DispatchObservation>.Empty);
    }

    private ReachedMarketContext LastExternalContext(DateTimeOffset closeTime)
    {
        Candle candle = _lastClosedCandle!;

        return new ReachedMarketContext(candle, _lastClosedCandleIndex, closeTime, PricePhase.External, candle.Close, checked(_lastClosedCandleIndex + 1));
    }

    private static bool SameIndicator(CompiledTradingNode old, CompiledTradingNode current)
    {
        if (old.Node.TypeKey != current.Node.TypeKey || old.Node.SchemaVersion != current.Node.SchemaVersion ||
            old.Node.Parameters.Count != current.Node.Parameters.Count || old.Connections.Count != current.Connections.Count ||
            old.EngineInputs.Count != current.EngineInputs.Count)
        {
            return false;
        }

        return old.Node.Parameters.All(pair => current.Node.Parameters.TryGetValue(pair.Key, out ParamValue? value) && Equals(pair.Value, value)) &&
            old.Connections.All(binding => current.Connections.Any(other => other.InputKey == binding.InputKey && other.Source == binding.Source)) &&
            old.EngineInputs.All(input => current.EngineInputs.Any(other => other.Spec.Key == input.Spec.Key && other.Source == input.Source &&
                Equals(other.Inline, input.Inline)));
    }

    /// <summary>
    ///     Publishes one update against the last closed candle. Its queued events are processed by the next Drain call.
    /// </summary>
    public IReadOnlyList<EventOccurrence> ApplyExternalOrderUpdate(
        ExternalOrderUpdate update,
        ReachedMarketContext context,
        DateTimeOffset? executionTime = null)
    {
        EnsureIdle();

        if (Account is not IExternallyExecutedAccount external)
        {
            throw new InvalidOperationException("External updates require an externally executed account.");
        }

        if (context.Phase != PricePhase.External || _lastClosedCandle != context.Candle)
        {
            throw new ArgumentException("External updates must use the last closed candle and External phase.", nameof(context));
        }

        _guard.BeginExternal(context.CandleIndex, context.MarketPrice);

        decimal before = Account.Capture().BaseTotal;
        IReadOnlyList<EventOccurrence> occurrences = ApplyOrderUpdateCore(update, context, external);
        decimal after = Account.Capture().BaseTotal;

        if (before != after)
        {
            DateTimeOffset? openingTime = null;

            if (executionTime is { } filledAt)
            {
                TimeSpan interval = context.CandleCloseTime - context.Candle.OpenTime;
                long elapsedTicks = (filledAt - context.Candle.OpenTime).Ticks;
                long candleOffset = Math.DivRem(elapsedTicks, interval.Ticks, out long remainder);

                if (remainder < 0)
                {
                    candleOffset--;
                }

                openingTime = context.Candle.OpenTime.AddTicks(checked(candleOffset * interval.Ticks));

                if (filledAt > context.CandleCloseTime + interval)
                {
                    _externalDiagnostics.Add(new ExternalOrderDiagnostic(update.OrderId, "ExternalFillExecutionTimeInFuture"));
                }
            }

            TrackPositionOpening(before, after, context, openingTime);
        }

        return occurrences;
    }

    /// <summary>
    ///     Submits an owner order against the last closed candle (ADR 0091): a market order, or a limit order when
    ///     <paramref name="limitPrice" /> is given, which the account rounds down to the tick. It is owned by no node; only
    ///     global listeners observe its lifecycle through the reserved tag, which also allows one working manual order. A
    ///     local rejection publishes no event.
    /// </summary>
    public OrderSubmission SubmitManualOrder(OrderSide side, decimal amount, decimal? limitPrice, ReachedMarketContext context)
    {
        EnsureIdle();

        if (Account is not IExternallyExecutedAccount)
        {
            throw new InvalidOperationException("Manual orders require an externally executed account.");
        }

        if (context.Phase != PricePhase.External || _lastClosedCandle != context.Candle)
        {
            throw new ArgumentException("Manual orders must use the last closed candle and External phase.", nameof(context));
        }

        _guard.BeginExternal(context.CandleIndex, context.MarketPrice);

        try
        {
            var request = new OrderRequest(
                side,
                limitPrice.HasValue ? OrderType.Limit : OrderType.Market,
                amount,
                limitPrice,
                ManualOrder.Tag,
                ManualOrder.OriginNodeId,
                context.EligibleFromCandleIndex,
                ReferencePrice: side == OrderSide.Buy && !limitPrice.HasValue ? context.MarketPrice : null);
            OrderSubmission result = Account.Submit(request);

            if (result.Order is { Status: OrderStatus.PendingSubmit } pending)
            {
                _pendingSubmissionAttempts.Add(pending.Id, checked(++_nextAttemptId));
            }

            return result;
        }
        catch
        {
            _faulted = true;

            throw;
        }
    }

    /// <summary>
    ///     The owner asks to cancel one working order (ADR 0048), whatever node or tag owns it. The order awaits cancellation as
    ///     when the strategy cancels it, so the venue's confirmation ends it; no node waits for the result, so no event is
    ///     published now and none completes later. Returns <see cref="ExternalCancelDisposition.NoTarget" /> for an order that is
    ///     not working and <see cref="ExternalCancelDisposition.AlreadyPending" /> for one already awaiting cancellation.
    /// </summary>
    public ExternalCancelDisposition RequestOrderCancel(long orderId)
    {
        EnsureIdle();

        if (Account is not IExternallyExecutedAccount external)
        {
            throw new InvalidOperationException("Owner cancels require an externally executed account.");
        }

        try
        {
            return external.RequestCancel(orderId).Disposition;
        }
        catch
        {
            _faulted = true;

            throw;
        }
    }

    internal IReadOnlyList<EventOccurrence> ApplySimulatedOrderUpdate(ExternalOrderUpdate update, ReachedMarketContext context)
    {
        EnsureIdle();

        if (Account is not IExternallyExecutedAccount external || context.Phase == PricePhase.External ||
            context.CandleIndex < _lastClosedCandleIndex || context.CandleIndex > _lastClosedCandleIndex + 1)
        {
            throw new ArgumentException("A simulated update requires the current replay candle and an external account.", nameof(context));
        }

        _guard.AdvancePrice(context.MarketPrice);

        decimal before = Account.Capture().BaseTotal;
        IReadOnlyList<EventOccurrence> occurrences = ApplyOrderUpdateCore(update, context, external);
        decimal after = Account.Capture().BaseTotal;

        if (before != after)
        {
            TrackPositionOpening(before, after, context, context.Candle.OpenTime);
        }

        return occurrences;
    }

    private IReadOnlyList<EventOccurrence> ApplyOrderUpdateCore(
        ExternalOrderUpdate update,
        ReachedMarketContext context,
        IExternallyExecutedAccount external)
    {
        try
        {
            long rejectionAttemptId = _pendingSubmissionAttempts.GetValueOrDefault(update.OrderId);

            if (update.Kind == ExternalOrderUpdateKind.Rejected && update.OwnerConfirmedAbsent && rejectionAttemptId == 0 &&
                Account.Capture().Orders.Any(order => order.Id == update.OrderId))
            {
                rejectionAttemptId = checked(++_nextAttemptId);
            }

            ExternalOrderUpdateResult result = external.ApplyExternalUpdate(update, rejectionAttemptId);

            if (result.DiagnosticCode is { } code)
            {
                _externalDiagnostics.Add(new ExternalOrderDiagnostic(update.OrderId, code));

                return Array.Empty<EventOccurrence>();
            }

            if (update.Kind == ExternalOrderUpdateKind.Accepted)
            {
                _pendingSubmissionAttempts.Remove(update.OrderId);
            }

            var published = new List<EventOccurrence>();

            foreach (LifecycleEmission emission in result.Emissions)
            {
                string owner = emission.Payload switch
                {
                    OrderPayload order => order.Order.OriginNodeId,
                    FillPayload fill => fill.OriginNodeId,
                    RejectionPayload rejection => rejection.Rejection.OriginNodeId,
                    _ => throw new ArgumentException("Unsupported external lifecycle emission.", nameof(update))
                };
                _guard.Consume(owner, causationId: null);
                EventOccurrence occurrence = Enqueue(emission.Kind, emission.Payload, context, cause: null);
                published.Add(occurrence);

                if (update.Kind != ExternalOrderUpdateKind.LocalAccepted &&
                    emission.Kind is EventKind.Submitted or EventKind.Rejected or EventKind.Filled or EventKind.Cancelled)
                {
                    _pendingSubmissionAttempts.Remove(update.OrderId);
                }

                if (emission.Kind is EventKind.Rejected or EventKind.Filled or EventKind.Cancelled &&
                    _pendingCancellations.Remove(update.OrderId, out (string NodeId, EventOccurrence Cause) pending))
                {
                    bool cancelled = emission.Kind == EventKind.Cancelled;
                    _guard.Consume(pending.NodeId, pending.Cause.Id);
                    published.Add(
                        Enqueue(
                            EventKind.CancelCompleted,
                            new CancelResultPayload(cancelled, update.OrderId, pending.NodeId, pending.Cause),
                            context,
                            occurrence.Id));
                }
            }

            return published.AsReadOnly();
        }
        catch
        {
            _faulted = true;

            throw;
        }
    }

    public EventOccurrence EnqueueExternal(EventKind kind, EventPayload payload, ReachedMarketContext context)
    {
        EnsureHealthy();

        if ((kind == EventKind.Start && (context.Phase != PricePhase.Open || payload is not StartPayload)) ||
            (kind == EventKind.CandleClosed && (context.Phase != PricePhase.Close || payload is not CandlePayload candle || candle.Candle != context.Candle)) ||
            kind is not (EventKind.Start or EventKind.CandleClosed))
        {
            throw new ArgumentException("Only replay Start and CandleClosed are external triggers.", nameof(kind));
        }

        if (GraphVersionId is not null && kind == EventKind.CandleClosed && !AreIndicatorsReady)
        {
            throw new InvalidOperationException("Indicators are not ready; no candle event can be dispatched.");
        }

        return Enqueue(kind, payload, context, cause: null);
    }

    public EventOccurrence PublishExecution(OrderSnapshot originalOrder, OrderExecution execution, ReachedMarketContext context)
    {
        EnsureHealthy();
        LastPublishedExecution = new ExecutionPublicationFact(originalOrder, execution, context);

        try
        {
            if (execution.Fill is not null)
            {
                decimal after = Account.Capture().BaseTotal;
                TrackPositionOpening(after - execution.Fill.BaseChange, after, context, context.Candle.OpenTime);

                _guard.Consume(originalOrder.OriginNodeId, causationId: null);

                return Enqueue(EventKind.Filled, new FillPayload(execution.Fill, originalOrder.OriginNodeId), context, cause: null);
            }

            if (execution.Rejection is not null)
            {
                _guard.Consume(originalOrder.OriginNodeId, causationId: null);

                return Enqueue(EventKind.Rejected, new RejectionPayload(checked(++_nextAttemptId), execution.Rejection), context, cause: null);
            }

            throw new ArgumentException("An execution must carry a fill or rejection.", nameof(execution));
        }
        catch
        {
            _faulted = true;

            throw;
        }
    }

    public DispatchBatch Drain(CancellationToken cancellationToken = default)
    {
        DrainCore(cancellationToken, retainProjections: true);

        if (GraphVersionId is not null)
        {
            RememberOrderVersions();
        }

        return LastBatch;
    }

    internal DispatchBatch DrainForReplay(CancellationToken cancellationToken)
    {
        DrainCore(cancellationToken, retainProjections: false);

        return LastBatch;
    }

    internal ReplayDispatchBatch DrainReplayIntoBuffers(CancellationToken cancellationToken)
    {
        DrainCore(cancellationToken, retainProjections: false);

        return _replayBatch;
    }

    private void DrainCore(CancellationToken cancellationToken, bool retainProjections)
    {
        EnsureHealthy();
        _isDraining = true;
        _observations.Clear();
        _occurrences.Clear();
        _skips.Clear();
        _facts.Clear();

        try
        {
            while (_queue.TryDequeue(out (EventOccurrence Occurrence, ReachedMarketContext Market) routed))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EventOccurrence occurrence = routed.Occurrence;
                _applied.Clear();
                AccountSnapshot? snapshot = null;
                _router.RouteInto(_graph, occurrence, _routedValues, _routedSlots);
                IReadOnlyDictionary<PortReference, EngineValue>? outputs = null;
                IReadOnlyList<ActionRequest> requests;
                IReadOnlyList<ActionSkipDiagnostic> skips;

                if (retainProjections)
                {
                    snapshot = Account.Capture();
                    var context = new DispatchEvaluationContext(snapshot, Account.Rules, Account.Settings, routed.Market, _positionOpenedAt, _closures);
                    outputs = _evaluator.ReadRoutedDispatch(occurrence, _routedValues, context);
                    ActionCollectionResult collection = _collector.Collect(
                        _graph,
                        occurrence,
                        routed.Market,
                        outputs,
                        _inputs);
                    requests = collection.Requests;
                    skips = collection.Skips;
                }
                else
                {
                    EngineValue[] replayOutputs = _evaluator.ReadDispatchForReplay(occurrence, _routedValues, Account, routed.Market, _positionOpenedAt, _closures);
                    _collector.CollectForReplay(
                        _graph,
                        occurrence,
                        routed.Market,
                        replayOutputs,
                        _evaluator,
                        _requestsScratch,
                        _skipsScratch);
                    requests = _requestsScratch;
                    skips = _skipsScratch;
                }

                for (var skipIndex = 0; skipIndex < skips.Count; skipIndex++)
                {
                    _skips.Add(skips[skipIndex]);
                }

                _occurrences.Add(occurrence);

                if (retainProjections)
                {
                    _observations.Add(new DispatchObservation(occurrence, snapshot!, outputs!, requests));
                }

                for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
                {
                    ActionRequest request = requests[requestIndex];
                    cancellationToken.ThrowIfCancellationRequested();

                    if (request is GridStartRequest or GridEvaluateRequest or GridCancelRequest)
                    {
                        ApplyGrid(request, occurrence, routed.Market);

                        continue;
                    }

                    if (!_applied.Add((occurrence.Id, request.NodeId)))
                    {
                        continue;
                    }

                    _guard.Consume(request.NodeId, occurrence.Id);
                    long attemptId = checked(++_nextAttemptId);
                    ActionApplication application = _executor.Apply(request, Account, occurrence, attemptId);
                    IReadOnlyList<LifecycleEmission> emissions = application.Emissions;

                    if (application.PendingSubmitOrderId is { } pendingSubmitOrderId)
                    {
                        _pendingSubmissionAttempts.Add(pendingSubmitOrderId, attemptId);
                    }

                    if (application.PendingCancelOrderId is { } pendingCancelOrderId)
                    {
                        _pendingCancellations.Add(pendingCancelOrderId, (request.NodeId, occurrence));
                    }

                    _facts.Add(new AppliedActionFact(request.NodeId, occurrence.Id, attemptId, emissions));

                    for (var emissionIndex = 0; emissionIndex < emissions.Count; emissionIndex++)
                    {
                        LifecycleEmission emission = emissions[emissionIndex];
                        _guard.Consume(request.NodeId, occurrence.Id);
                        Enqueue(emission.Kind, emission.Payload, routed.Market, occurrence.Id);
                    }
                }
            }
        }
        catch
        {
            _faulted = true;

            throw;
        }
        finally
        {
            _isDraining = false;
        }
    }

    /// <summary>
    ///     Starts, books, stops and reconciles a grid (ADR 0058). Its submits and cancels are ordinary account actions with their
    ///     own attempts; a grid's pending cancel waits for no completion, because the grid reacts to the cancellation itself.
    /// </summary>
    private void ApplyGrid(ActionRequest request, EventOccurrence occurrence, ReachedMarketContext market)
    {
        switch (request)
        {
            case GridStartRequest start:
                if (Grid(start.NodeId).Start(occurrence.MarketPrice))
                {
                    _guard.Consume(start.NodeId, occurrence.Id);
                    Enqueue(EventKind.GridStarted, new GridPayload(start.NodeId, occurrence.MarketPrice), market, occurrence.Id);
                }

                return;
            case GridCancelRequest cancel:
                if (_grids.TryGetValue(cancel.GridNodeId, out GridState? cancelled) && cancelled.Phase == GridPhase.Running)
                {
                    cancelled.RequestStop(sell: false);
                    PlanGrid(cancelled, settings: null, occurrence, market);
                }

                _guard.Consume(cancel.NodeId, occurrence.Id);
                Enqueue(EventKind.CancelCompleted, new CancelResultPayload(Cancelled: true, CancelledOrderId: null, cancel.NodeId, occurrence), market, occurrence.Id);

                return;
            case GridEvaluateRequest evaluate:
                GridState grid = Grid(evaluate.NodeId);

                if (grid.Book(occurrence, Account))
                {
                    EmitGridStopped(grid, occurrence, market);

                    return;
                }

                if (occurrence.Kind == EventKind.CandleClosed && evaluate.Settings is { } settings)
                {
                    grid.OnClose(settings, occurrence.MarketPrice, Account, Account.Capture());
                }

                PlanGrid(grid, evaluate.Settings, occurrence, market);

                return;
            default:
                throw new ArgumentException("Unknown grid request.", nameof(request));
        }
    }

    private void PlanGrid(GridState grid, GridSettings? settings, EventOccurrence occurrence, ReachedMarketContext market)
    {
        IReadOnlyList<ActionRequest> plan = grid.Plan(settings, Account, Account.Capture(), market, out bool stopped);

        foreach (ActionRequest action in plan)
        {
            _guard.Consume(action.NodeId, occurrence.Id);
            long attemptId = checked(++_nextAttemptId);
            ActionApplication application = _executor.Apply(action, Account, occurrence, attemptId);

            if (application.PendingSubmitOrderId is { } pendingSubmitOrderId)
            {
                _pendingSubmissionAttempts.Add(pendingSubmitOrderId, attemptId);
            }

            _facts.Add(new AppliedActionFact(action.NodeId, occurrence.Id, attemptId, application.Emissions));

            foreach (LifecycleEmission emission in application.Emissions)
            {
                _guard.Consume(action.NodeId, occurrence.Id);
                Enqueue(emission.Kind, emission.Payload, market, occurrence.Id);
            }
        }

        if (stopped)
        {
            EmitGridStopped(grid, occurrence, market);
        }
    }

    private void EmitGridStopped(GridState grid, EventOccurrence occurrence, ReachedMarketContext market)
    {
        _guard.Consume(grid.NodeId, occurrence.Id);
        Enqueue(EventKind.GridStopped, new GridPayload(grid.NodeId, Anchor: null), market, occurrence.Id);
    }

    private GridState Grid(string nodeId)
    {
        if (!_grids.TryGetValue(nodeId, out GridState? grid))
        {
            grid = new GridState(nodeId);
            _grids.Add(nodeId, grid);
        }

        return grid;
    }

    private EventOccurrence Enqueue(EventKind kind, EventPayload payload, ReachedMarketContext context, long? cause)
    {
        long id = checked(++_nextEventId);
        var occurrence = new EventOccurrence(
            id,
            id,
            cause,
            kind,
            context.CandleIndex,
            context.Candle.OpenTime,
            context.CandleCloseTime,
            context.Phase,
            context.MarketPrice,
            payload);
        _queue.Enqueue((occurrence, context));

        return occurrence;
    }

    /// <summary>
    ///     Adds a market closure a live venue reported after the last one, so position age leaves it out (ADR 0023, TC3).
    /// </summary>
    public void RecordClosure(MarketClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);

        if (closure.From >= closure.To || (_closures.Count > 0 && closure.From < _closures[^1].To))
        {
            throw new ArgumentException("A live market closure must be nonempty and follow the last one.", nameof(closure));
        }

        _closures.Add(closure);
    }

    private void TrackPositionOpening(decimal before, decimal after, ReachedMarketContext context, DateTimeOffset? openingTime)
    {
        if (PositionHoldingPeriod.IsFlat(after, Account.Rules, Account.Settings, context.MarketPrice))
        {
            _positionOpenedAt = null;
        }
        else if (PositionHoldingPeriod.IsFlat(before, Account.Rules, Account.Settings, context.MarketPrice))
        {
            _positionOpenedAt = openingTime;
        }
    }

    private void EnsureIdle()
    {
        EnsureHealthy();

        if (_queue.Count != 0 || _isDraining)
        {
            throw new InvalidOperationException("Causal work must drain before advancing the market cursor or indicator clock.");
        }
    }

    private void EnsureHealthy()
    {
        if (_faulted)
        {
            throw new InvalidOperationException("A dispatcher cannot continue after cancellation or runtime failure.");
        }
    }
}