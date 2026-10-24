using System.Collections.Immutable;
using System.Collections.ObjectModel;

using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     Immutable typed bindings, parameter schema and deterministic execution clock.
/// </summary>
public sealed class CompiledTradingNode
{
    internal readonly bool IsPureComputation;
    internal readonly bool EvaluatesDispatch;
    internal readonly bool ProjectsTradingState;
    internal readonly bool IsIndicator;
    internal readonly bool IsCrossover;
    internal readonly bool IsCandleClocked;
    internal readonly bool EvaluatesUnavailableInputs;

    public GraphNode Node { get; }
    public INodeDefinition Definition { get; }
    public int DependencyRank { get; }
    public NodeClock Clock { get; }
    public TradingNodeBehavior? TradingBehavior { get; }
    public EventFieldSpec? EventField { get; }
    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; }
    public IReadOnlyList<ParamSpec> ParameterSpecs { get; }
    public IReadOnlyList<CompiledEngineInput> EngineInputs { get; }
    public IReadOnlyList<CompiledInputBinding> Connections { get; }
    public IReadOnlyDictionary<string, ValueType> OutputTypes { get; }
    internal CompiledInputBinding? TriggerBinding { get; }
    internal ValueType FirstOutputType { get; }
    internal ImmutableArray<KeyValuePair<PortReference, ValueType>> OutputBindings { get; }
    internal ImmutableArray<KeyValuePair<int, ValueType>> OutputSlots { get; }
    internal IReadOnlyDictionary<string, int> OutputSlotsByKey { get; }
    internal IReadOnlyDictionary<string, int> WiredParameterSourceSlots { get; }
    internal bool HasDynamicParameters { get; }
    internal IReadOnlyList<ParamSpec> UnboundParameterSpecs { get; }
    internal IReadOnlyDictionary<string, PortReference> OutputReferences { get; }

    internal CompiledTradingNode(
        GraphNode node,
        INodeDefinition definition,
        int dependencyRank,
        NodeClock clock,
        IEnumerable<ParamSpec> parameterSpecs,
        IEnumerable<CompiledEngineInput> engineInputs,
        IEnumerable<CompiledInputBinding> connections,
        IDictionary<string, ValueType> outputTypes,
        TradingNodeBehavior? tradingBehavior,
        EventFieldSpec? eventField,
        IEnumerable<ActionEvaluationMode>? allowedEvaluationModes,
        IReadOnlyDictionary<PortReference, int> slots)
    {
        Node = node;
        Definition = definition;
        IsPureComputation = definition.IsPureComputation;
        EvaluatesDispatch = definition.EvaluatesDispatch;
        ProjectsTradingState = definition.ProjectsTradingState;
        IsIndicator = definition.IsIndicator;
        IsCrossover = definition.IsCrossover;
        IsCandleClocked = definition.IsCandleClocked;
        EvaluatesUnavailableInputs = definition.EvaluatesUnavailableInputs;
        DependencyRank = dependencyRank;
        Clock = clock;
        TradingBehavior = tradingBehavior;
        EventField = eventField;
        AllowedEvaluationModes = Array.AsReadOnly(allowedEvaluationModes?.ToArray() ?? Array.Empty<ActionEvaluationMode>());
        ParameterSpecs = Array.AsReadOnly(
            parameterSpecs.Select(spec => spec with
                {
                    AllowedValues = spec.AllowedValues is null ? null : Array.AsReadOnly(spec.AllowedValues.ToArray())
                })
                .ToArray());
        EngineInputs = Array.AsReadOnly(engineInputs.ToArray());
        HasDynamicParameters = Node.Parameters.Values.Any(value => value is WiredParamValue) ||
            EngineInputs.Any(input => input.Source is not null || input.Inline is WiredParamValue);
        UnboundParameterSpecs =
            Array.AsReadOnly(ParameterSpecs.Where(spec => !EngineInputs.Any(input => input.Spec.Key == spec.Key && input.Source is not null)).ToArray());
        Connections = Array.AsReadOnly(connections.ToArray());
        TriggerBinding = Connections.SingleOrDefault(binding => binding.InputKey == "trigger");
        OutputTypes = new ReadOnlyDictionary<string, ValueType>(new Dictionary<string, ValueType>(outputTypes, StringComparer.Ordinal));
        OutputReferences =
            new ReadOnlyDictionary<string, PortReference>(outputTypes.Keys.ToDictionary(key => key, key => new PortReference(node.Id, key), StringComparer.Ordinal));
        OutputBindings = outputTypes.Select(pair => new KeyValuePair<PortReference, ValueType>(OutputReferences[pair.Key], pair.Value)).ToImmutableArray();
        OutputSlots = OutputBindings.Select(pair => new KeyValuePair<int, ValueType>(slots[pair.Key], pair.Value)).ToImmutableArray();
        OutputSlotsByKey = new ReadOnlyDictionary<string, int>(OutputReferences.ToDictionary(pair => pair.Key, pair => slots[pair.Value], StringComparer.Ordinal));
        WiredParameterSourceSlots = new ReadOnlyDictionary<string, int>(
            UnboundParameterSpecs.Where(spec => spec.ValueKind != ParamValueKind.Text && Node.Parameters.GetValueOrDefault(spec.Key) is WiredParamValue)
                .ToDictionary(spec => spec.Key, spec => slots[((WiredParamValue)Node.Parameters[spec.Key]).Source], StringComparer.Ordinal));
        FirstOutputType = outputTypes.Values.FirstOrDefault();
    }
}