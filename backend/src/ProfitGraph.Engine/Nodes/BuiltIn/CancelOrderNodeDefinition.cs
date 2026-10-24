using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the CancelOrder node.
/// </summary>
public sealed class CancelOrderNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.CancelOrder);
    public bool IsAction => true;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.CancelOrder;
    public NodeClock? EngineClock => NodeClock.Action;
    public NodeCategory Category => NodeCategory.Signal;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("trigger", PortKind.Event, "trigger", ValueType.Event),
            new PortSpec("enabled", PortKind.Condition, "enabled", ValueType.Boolean),
            new PortSpec("orderId", PortKind.Series, "orderId", ValueType.OrderId),
            new PortSpec("tag", PortKind.Series, "tag", ValueType.Tag)
        });

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("completed", PortKind.Event, "Completed", ValueType.Event, EventPayloadType.CancelResult),
            new PortSpec("rejected", PortKind.Event, "Rejected", ValueType.Event, EventPayloadType.Rejection)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.Empty<ParamSpec>();

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.AsReadOnly(
        new[]
        {
            new EngineInputSpec("enabled", ValueType.Boolean, required: false, supportsInline: true, EngineValue.FromBoolean(true)),
            new EngineInputSpec("orderId", ValueType.OrderId, required: false, supportsInline: true),
            new EngineInputSpec("tag", ValueType.Tag, required: false, supportsInline: true)
        });

    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.AsReadOnly(new[] { ActionEvaluationMode.OnEvent });

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Action;
    }
}