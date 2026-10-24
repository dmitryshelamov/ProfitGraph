using System.Collections.ObjectModel;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Per-run decimal evaluation with one indicator advance per closed candle and immutable dispatch projections.
/// </summary>
public sealed class CandleValueEvaluator
{
    private readonly CompiledTradingGraph _graph;
    private readonly EngineExecutionInputs _execution;
    private readonly EngineParameterResolver _resolver = new EngineParameterResolver();
    private readonly EngineValue[] _cache;
    private readonly Dictionary<string, CandleIndicatorState> _indicators = new Dictionary<string, CandleIndicatorState>(StringComparer.Ordinal);
    private readonly EngineValue[] _dispatchOutputs;
    private readonly ResolvedEngineParameters?[] _parameterStorage;

    private readonly Dictionary<string, EngineValue>?[] _nodeInputs;
    private readonly (ResolvedEngineParameters Parameters, long ParameterVersion, EngineValue Value)?[] _computedValues;
    private readonly Dictionary<string, EngineValue>?[] _stateOutputs;
    private readonly CompiledTradingNode[] _slotOwners;
    private readonly CompiledInputBinding[][] _replayConnections;
    private readonly int[][] _parameterSourceSlots;
    private readonly long[] _dispatchStamps;

    private readonly HashSet<PortReference> _unreadHoldingPeriodOutputs;

    private readonly TradingValueEvaluator _tradingEvaluator = new TradingValueEvaluator();
    private long _dispatchStamp;
    private EventOccurrence? _replayOccurrence;
    private EngineValue[]? _replayExternalValues;
    private DispatchEvaluationContext? _replayContext;
    private IGraphAccount? _replayAccount;
    private ReachedMarketContext? _replayMarket;
    private bool _replayClose;
    private DateTimeOffset? _replayPositionOpenedAt;
    private Candle? _lastCandle;
    private int _candleIndex = -1;
    private bool _faulted;

    /// <summary>
    ///     Whether all sampled indicators have sufficient available history at the current closed candle.
    /// </summary>
    public bool AreIndicatorsReady => _graph.IndicatorNodes
        .All(node => _indicators.TryGetValue(node.Node.Id, out CandleIndicatorState? state) && state.Ready);

    private DispatchEvaluationContext ReplayContext => _replayContext ??= new DispatchEvaluationContext(
        _replayAccount!.Capture(),
        _replayAccount!.Rules,
        _replayAccount.Settings,
        _replayMarket!,
        _replayPositionOpenedAt);

    public CandleValueEvaluator(CompiledTradingGraph graph, EngineExecutionInputs? execution = null)
    {
        _graph = graph;
        _execution = execution ?? EngineExecutionInputs.Empty;
        HashSet<PortReference> readOutputs = graph.Nodes.SelectMany(node => node.Connections.Select(binding => binding.Source)
                .Concat(node.EngineInputs.Where(input => input.Source is not null).Select(input => input.Source!)))
            .ToHashSet();
        _unreadHoldingPeriodOutputs = graph.Nodes.Where(node => node.TradingBehavior == TradingNodeBehavior.Position)
            .Select(node => new PortReference(node.Node.Id, "barsHeld"))
            .Where(reference => !readOutputs.Contains(reference))
            .ToHashSet();
        GraphValidationResult validation = _resolver.ValidateSelections(graph, _execution);

        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Diagnostics.Select(diagnostic => diagnostic.Message)), nameof(execution));
        }

        _cache = new EngineValue[graph.SlotCount];
        _dispatchOutputs = new EngineValue[graph.SlotCount];
        _parameterStorage = new ResolvedEngineParameters?[graph.Nodes.Count];
        _nodeInputs = new Dictionary<string, EngineValue>?[graph.Nodes.Count];
        _computedValues = new (ResolvedEngineParameters Parameters, long ParameterVersion, EngineValue Value)?[graph.Nodes.Count];
        _stateOutputs = new Dictionary<string, EngineValue>?[graph.Nodes.Count];
        _slotOwners = new CompiledTradingNode[graph.SlotCount];
        _replayConnections = new CompiledInputBinding[graph.Nodes.Count][];
        _dispatchStamps = new long[graph.Nodes.Count];
        _parameterSourceSlots = new int[graph.Nodes.Count][];

        foreach (CompiledTradingNode node in graph.Nodes)
        {
            foreach ((int slot, ValueType type) in node.OutputSlots)
            {
                _slotOwners[slot] = node;
                _cache[slot] = EngineValue.Unavailable(type);
                _dispatchOutputs[slot] = EngineValue.Unavailable(type);
            }
        }

        foreach (CompiledTradingNode node in graph.Nodes)
        {
            _parameterSourceSlots[node.DependencyRank] = node.WiredParameterSourceSlots.Values
                .Concat(node.EngineInputs.Where(input => input.SourceSlot >= 0).Select(input => input.SourceSlot))
                .Distinct()
                .ToArray();
            _replayConnections[node.DependencyRank] = node.Connections
                .OrderBy(binding => _slotOwners[binding.SourceSlot].OutputTypes[binding.Source.PortKey] == ValueType.Event ? 0 : 1)
                .ToArray();
        }

        for (var nodeIndex = 0; nodeIndex < graph.StaticNodes.Count; nodeIndex++)
        {
            CompiledTradingNode node = graph.StaticNodes[nodeIndex];
            EvaluateNode(node, _cache, candle: null, advance: false);
        }
    }

    /// <summary>
    ///     Returns captured static values for template preflight without synthetic candles or dispatches.
    /// </summary>
    public IReadOnlyDictionary<PortReference, EngineValue> ReadStaticValues()
    {
        return Snapshot(_cache);
    }

    public IReadOnlyDictionary<PortReference, EngineValue> AdvanceClosedCandle(Candle candle, int candleIndex)
    {
        CommitClosedCandle(candle, candleIndex);

        return Snapshot(_cache);
    }

    internal void CommitClosedCandle(Candle candle, int candleIndex)
    {
        if (_faulted || candleIndex != _candleIndex + 1 || candle.OpenTime.Offset != TimeSpan.Zero ||
            (_lastCandle is not null && candle.OpenTime <= _lastCandle.OpenTime))
        {
            throw new ArgumentException("Closed candles must advance once in strictly increasing UTC order.", nameof(candle));
        }

        _replayOccurrence = null;
        _candleIndex = candleIndex;

        try
        {
            for (var nodeIndex = 0; nodeIndex < _graph.CandleNodes.Count; nodeIndex++)
            {
                CompiledTradingNode node = _graph.CandleNodes[nodeIndex];
                EvaluateNode(node, _cache, candle, advance: true);
            }

            _lastCandle = candle;
        }
        catch
        {
            _faulted = true;

            throw;
        }
    }

    internal void RelabelRestoredCandleIndex(int candleIndex)
    {
        if (_lastCandle is null || candleIndex < _candleIndex)
        {
            throw new ArgumentException("Restored candle index must follow supplied history.", nameof(candleIndex));
        }

        _candleIndex = candleIndex;
    }

    internal void KeepIndicatorState(CandleValueEvaluator previous, string nodeId)
    {
        if (!previous._indicators.TryGetValue(nodeId, out CandleIndicatorState? state) ||
            !_graph.NodesById.TryGetValue(nodeId, out CompiledTradingNode? current) ||
            !previous._graph.NodesById.TryGetValue(nodeId, out CompiledTradingNode? old))
        {
            return;
        }

        _indicators[nodeId] = state;

        foreach (string key in current.OutputSlotsByKey.Keys)
        {
            if (old.OutputSlotsByKey.TryGetValue(key, out int oldSlot))
            {
                _cache[current.OutputSlotsByKey[key]] = previous._cache[oldSlot];
            }
        }
    }

    public IReadOnlyDictionary<PortReference, EngineValue> ReadDispatch(
        EventOccurrence occurrence,
        IReadOnlyDictionary<PortReference, EngineValue>? externalValues = null,
        DispatchEvaluationContext? dispatch = null)
    {
        IReadOnlyDictionary<PortReference, EngineValue>? external = externalValues is null
            ? null
            : new Dictionary<PortReference, EngineValue>(externalValues);

        return Snapshot(EvaluateDispatch(occurrence, externalSlots: null, external, dispatch));
    }

    internal EngineValue[] ReadDispatchForReplay(EventOccurrence occurrence, EngineValue[] externalValues, DispatchEvaluationContext? dispatch)
    {
        EnsureHealthy();
        _replayOccurrence = occurrence;
        _replayExternalValues = externalValues;
        _replayContext = dispatch;
        _replayAccount = null;
        _replayMarket = null;
        _replayClose = IsMatchingClose(occurrence);

        if (_dispatchStamp == long.MaxValue)
        {
            Array.Clear(_dispatchStamps);
            _dispatchStamp = 0;
        }

        _dispatchStamp++;

        return _dispatchOutputs;
    }

    internal EngineValue[] ReadDispatchForReplay(
        EventOccurrence occurrence,
        EngineValue[] externalValues,
        IGraphAccount account,
        ReachedMarketContext market,
        DateTimeOffset? positionOpenedAt = null)
    {
        EngineValue[] outputs = ReadDispatchForReplay(occurrence, externalValues, dispatch: null);
        _replayAccount = account;
        _replayMarket = market;
        _replayPositionOpenedAt = positionOpenedAt;

        return outputs;
    }

    internal IReadOnlyDictionary<PortReference, EngineValue> ReadRoutedDispatch(
        EventOccurrence occurrence,
        EngineValue[] externalValues,
        DispatchEvaluationContext dispatch)
    {
        return Snapshot(EvaluateDispatch(occurrence, externalValues, externalValues: null, dispatch));
    }

    internal ResolvedEngineParameters ResolveReplayParameters(CompiledTradingNode node, EngineValue[] outputs)
    {
        if (_replayOccurrence is not null)
        {
            foreach (int slot in _parameterSourceSlots[node.DependencyRank])
            {
                ReadReplayValue(slot, outputs);
            }
        }

        return _resolver.ResolveForReplay(node, outputs, _execution, _parameterStorage, node.DependencyRank);
    }

    internal EngineValue ReadReplayValue(int slot, EngineValue[] outputs)
    {
        if (_replayOccurrence is null)
        {
            return outputs[slot];
        }

        CompiledTradingNode node = _slotOwners[slot];

        if (_dispatchStamps[node.DependencyRank] != _dispatchStamp)
        {
            EvaluateReplayNode(node);
            _dispatchStamps[node.DependencyRank] = _dispatchStamp;
        }

        return _dispatchOutputs[slot];
    }

    private void EvaluateReplayNode(CompiledTradingNode node)
    {
        EventOccurrence occurrence = _replayOccurrence!;

        try
        {
            if (node.IsCrossover)
            {
                foreach ((int slot, _) in node.OutputSlots)
                {
                    _dispatchOutputs[slot] = _replayClose ? _cache[slot] : EngineValue.FromBoolean(false);
                }

                return;
            }

            if (node.Clock == NodeClock.Static || (node.Clock == NodeClock.CandleClocked && node.IsCandleClocked))
            {
                foreach ((int slot, _) in node.OutputSlots)
                {
                    _dispatchOutputs[slot] = _cache[slot];
                }

                return;
            }

            if (node.ProjectsTradingState)
            {
                Dictionary<string, EngineValue> stateOutputs = _stateOutputs[node.DependencyRank] ??= new Dictionary<string, EngineValue>(StringComparer.Ordinal);
                _tradingEvaluator.EvaluateForReplay(node, ResolveReplayParameters(node, _dispatchOutputs), ReplayContext, stateOutputs, _dispatchOutputs);

                return;
            }

            if (node.Clock is NodeClock.Dispatch or NodeClock.Event or NodeClock.Action && !node.IsPureComputation && !node.EvaluatesDispatch)
            {
                foreach ((int slot, _) in node.OutputSlots)
                {
                    _dispatchOutputs[slot] = _replayExternalValues![slot];
                }

                return;
            }

            foreach (CompiledInputBinding binding in _replayConnections[node.DependencyRank])
            {
                EngineValue value = ReadReplayValue(binding.SourceSlot, _dispatchOutputs);

                // FilterEvent's unavailable event makes its output independent of the condition.
                if (!value.IsAvailable && (!node.EvaluatesUnavailableInputs || (node.Node.TypeKey == NodeTypeKeys.FilterEvent && binding.InputKey == "event")))
                {
                    Set(node, _dispatchOutputs, EngineValue.Unavailable(node.FirstOutputType));

                    return;
                }
            }

            EvaluateNode(node, _dispatchOutputs, _lastCandle, advance: false, occurrence, _replayClose);
        }
        catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            throw new GraphExecutionException(node.Node.Id, occurrence.CandleIndex, exception.Message, exception, occurrence.Id);
        }
    }

    private bool IsMatchingClose(EventOccurrence occurrence)
    {
        return occurrence.Kind == EventKind.CandleClosed && occurrence.Phase == PricePhase.Close &&
            occurrence.CandleOpenTime == _lastCandle?.OpenTime && occurrence.Payload is CandlePayload payload && payload.Candle == _lastCandle;
    }

    private void EnsureHealthy()
    {
        if (_faulted)
        {
            throw new InvalidOperationException("An evaluator cannot continue after an execution failure.");
        }
    }

    private EngineValue[] EvaluateDispatch(
        EventOccurrence occurrence,
        EngineValue[]? externalSlots,
        IReadOnlyDictionary<PortReference, EngineValue>? externalValues,
        DispatchEvaluationContext? dispatch)
    {
        EnsureHealthy();
        _replayOccurrence = null;
        EngineValue[] outputs = _dispatchOutputs;

        foreach (int slot in _graph.CachedOutputs)
        {
            outputs[slot] = _cache[slot];
        }

        for (var nodeIndex = 0; nodeIndex < _graph.DispatchNodes.Count; nodeIndex++)
        {
            CompiledTradingNode node = _graph.DispatchNodes[nodeIndex];

            foreach ((int slot, ValueType type) in node.OutputSlots)
            {
                outputs[slot] = externalSlots is null ? EngineValue.Unavailable(type) : externalSlots[slot];
            }
        }

        if (externalValues is not null)
        {
            foreach ((PortReference key, EngineValue value) in externalValues)
            {
                if (!_graph.NodesById.TryGetValue(key.NodeId, out CompiledTradingNode? node) ||
                    !node.OutputTypes.TryGetValue(key.PortKey, out ValueType expected) || value.ValueType != expected ||
                    node.Clock is NodeClock.Static or NodeClock.CandleClocked)
                {
                    throw new ArgumentException("Dispatch snapshot values must match compiled state/event output types.", nameof(externalValues));
                }

                outputs[node.OutputSlotsByKey[key.PortKey]] = value;
            }
        }

        bool close = IsMatchingClose(occurrence);

        for (var nodeIndex = 0; nodeIndex < _graph.Nodes.Count; nodeIndex++)
        {
            CompiledTradingNode node = _graph.Nodes[nodeIndex];

            try
            {
                if (node.IsCrossover)
                {
                    if (!close)
                    {
                        Set(node, outputs, EngineValue.FromBoolean(false));
                    }

                    continue;
                }

                if (node.Clock == NodeClock.Static || (node.Clock == NodeClock.CandleClocked && node.IsCandleClocked))
                {
                    continue;
                }

                if (node.ProjectsTradingState)
                {
                    if (dispatch is not null)
                    {
                        Dictionary<string, EngineValue> stateOutputs = _stateOutputs[node.DependencyRank] ??= new Dictionary<string, EngineValue>(StringComparer.Ordinal);

                        _tradingEvaluator.EvaluateForReplay(node, ResolveReplayParameters(node, outputs), dispatch, stateOutputs, outputs);
                    }
                }
                else if (node.Clock is NodeClock.Dispatch or NodeClock.Event or NodeClock.Action && !node.IsPureComputation &&
                         !node.EvaluatesDispatch)
                {
                    // Routed lifecycle values were seeded before dependency traversal.
                }
                else
                {
                    EvaluateNode(node, outputs, _lastCandle, advance: false, occurrence, close);
                }
            }
            catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                throw new GraphExecutionException(node.Node.Id, occurrence.CandleIndex, exception.Message, exception, occurrence.Id);
            }
        }

        return outputs;
    }

    private void EvaluateNode(
        CompiledTradingNode node,
        EngineValue[] outputs,
        Candle? candle,
        bool advance,
        EventOccurrence? occurrence = null,
        bool isCandleClose = false)
    {
        try
        {
            ResolvedEngineParameters parameters = ResolveReplayParameters(node, outputs);

            Dictionary<string, EngineValue> inputs = _nodeInputs[node.DependencyRank] ??= new Dictionary<string, EngineValue>(StringComparer.Ordinal);

            var changed = false;

            for (var inputIndex = 0; inputIndex < node.Connections.Count; inputIndex++)
            {
                CompiledInputBinding binding = node.Connections[inputIndex];
                EngineValue value = outputs[binding.SourceSlot];

                if (!inputs.TryGetValue(binding.InputKey, out EngineValue previous) || !previous.Equals(value))
                {
                    changed = true;
                    inputs[binding.InputKey] = value;
                }
            }

            for (var inputIndex = 0; inputIndex < node.EngineInputs.Count; inputIndex++)
            {
                CompiledEngineInput binding = node.EngineInputs[inputIndex];
                EngineValue value = parameters[binding.Spec.Key];

                if (!inputs.TryGetValue(binding.Spec.Key, out EngineValue previous) || !previous.Equals(value))
                {
                    changed = true;
                    inputs[binding.Spec.Key] = value;
                }
            }

            bool cacheable = !advance && node.IsPureComputation;

            if (advance)
            {
                _computedValues[node.DependencyRank] = null;
            }

            if (cacheable && !changed && _computedValues[node.DependencyRank] is { } cached &&
                ReferenceEquals(cached.Parameters, parameters) && cached.ParameterVersion == parameters.Version)
            {
                Set(node, outputs, cached.Value);

                return;
            }

            if (cacheable)
            {
                _computedValues[node.DependencyRank] = null;
            }

            ValueType type = node.FirstOutputType;
            EngineValue result;

            if ((HasUnavailableInputs(inputs) || parameters.HasUnavailableValues) && !node.EvaluatesUnavailableInputs)
            {
                result = EngineValue.Unavailable(type);
            }
            else
            {
                result = node.Definition.EvaluateEngine(
                    new NodeEngineEvaluationContext(
                        node,
                        inputs,
                        parameters,
                        candle,
                        _lastCandle,
                        occurrence,
                        isCandleClose,
                        node.IsIndicator ? State(node.Node.Id) : null));
            }

            Set(node, outputs, result);

            if (cacheable)
            {
                _computedValues[node.DependencyRank] = (parameters, parameters.Version, result);
            }
        }
        catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            throw new GraphExecutionException(node.Node.Id, occurrence?.CandleIndex ?? _candleIndex, exception.Message, exception, occurrence?.Id);
        }
    }

    private CandleIndicatorState State(string nodeId)
    {
        if (!_indicators.TryGetValue(nodeId, out CandleIndicatorState? state))
        {
            state = new CandleIndicatorState();
            _indicators[nodeId] = state;
        }

        return state;
    }

    private static bool HasUnavailableInputs(Dictionary<string, EngineValue> inputs)
    {
        foreach (EngineValue value in inputs.Values)
        {
            if (!value.IsAvailable)
            {
                return true;
            }
        }

        return false;
    }

    private static void Set(CompiledTradingNode node, EngineValue[] outputs, EngineValue value)
    {
        foreach ((int slot, ValueType type) in node.OutputSlots)
        {
            if (value.ValueType != type)
            {
                throw new InvalidOperationException("Runtime output does not match its compiled units.");
            }

            outputs[slot] = value;
        }
    }

    private IReadOnlyDictionary<PortReference, EngineValue> Snapshot(EngineValue[] values)
    {
        var snapshot = new Dictionary<PortReference, EngineValue>(_graph.SlotCount);

        foreach (CompiledTradingNode node in _graph.Nodes)
        {
            for (var index = 0; index < node.OutputBindings.Length; index++)
            {
                PortReference reference = node.OutputBindings[index].Key;

                // Preserve eager diagnostic snapshots of graphs authored before this optional position field existed.
                if (!_unreadHoldingPeriodOutputs.Contains(reference))
                {
                    snapshot.Add(reference, values[node.OutputSlots[index].Key]);
                }
            }
        }

        return new ReadOnlyDictionary<PortReference, EngineValue>(snapshot);
    }
}