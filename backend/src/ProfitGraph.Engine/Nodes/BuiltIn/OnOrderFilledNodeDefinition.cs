using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the OnOrderFilled node.
/// </summary>
public sealed class OnOrderFilledNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.OnOrderFilled);
    public bool SupportsPhase1 => false;
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.OnOrderFilled;
    public NodeClock? EngineClock => NodeClock.Event;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;
    public IReadOnlyList<PortSpec> Inputs { get; } = Array.Empty<PortSpec>();

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("out", PortKind.Event, "out", ValueType.Event, EventPayloadType.Fill)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.AsReadOnly(
        new[]
        {
            new ParamSpec("tag", "tag", ParamValueKind.Text, ""),
            new ParamSpec("side", "side", ParamValueKind.Text, "Any", AllowedValues: Array.AsReadOnly(new[] { "Any", "Buy", "Sell" }))
        });

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.Empty<EngineInputSpec>();
    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.Empty<ActionEvaluationMode>();

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Event;
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        throw new InvalidOperationException("A production trading node requires the causal dispatcher.");
    }
}