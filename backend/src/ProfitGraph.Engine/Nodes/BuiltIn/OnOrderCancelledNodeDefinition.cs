using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the OnOrderCancelled node.
/// </summary>
public sealed class OnOrderCancelledNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.OnOrderCancelled);
    public bool IsAction => false;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.OnOrderCancelled;
    public NodeClock? EngineClock => NodeClock.Event;
    public NodeCategory Category => NodeCategory.Risk;
    public int SchemaVersion => 1;
    public IReadOnlyList<PortSpec> Inputs { get; } = Array.Empty<PortSpec>();

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("out", PortKind.Event, "out", ValueType.Event, EventPayloadType.Order)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.AsReadOnly(
        new[]
        {
            new ParamSpec("tag", "tag", ParamValueKind.Text, "")
        });

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.Empty<EngineInputSpec>();
    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.Empty<ActionEvaluationMode>();

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Event;
    }
}