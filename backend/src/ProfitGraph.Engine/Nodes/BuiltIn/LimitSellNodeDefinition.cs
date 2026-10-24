using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the LimitSell node.
/// </summary>
public sealed class LimitSellNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.LimitSell);
    public bool IsAction => true;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.LimitSell;
    public NodeClock? EngineClock => NodeClock.Action;
    public NodeCategory Category => NodeCategory.Signal;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("trigger", PortKind.Event, "trigger", ValueType.Event),
            new PortSpec("enabled", PortKind.Condition, "enabled", ValueType.Boolean),
            new PortSpec("quantity", PortKind.Series, "quantity", ValueType.BaseAmount),
            new PortSpec("tag", PortKind.Series, "tag", ValueType.Tag),
            new PortSpec("price", PortKind.Series, "price", ValueType.Price)
        });

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("submitted", PortKind.Event, "Submitted", ValueType.Event, EventPayloadType.Order),
            new PortSpec("filled", PortKind.Event, "Filled", ValueType.Event, EventPayloadType.Fill),
            new PortSpec("cancelled", PortKind.Event, "Cancelled", ValueType.Event, EventPayloadType.Order),
            new PortSpec("rejected", PortKind.Event, "Rejected", ValueType.Event, EventPayloadType.Rejection)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.Empty<ParamSpec>();

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.AsReadOnly(
        new[]
        {
            new EngineInputSpec("enabled", ValueType.Boolean, required: false, supportsInline: true, EngineValue.FromBoolean(true)),
            new EngineInputSpec("quantity", ValueType.BaseAmount, required: true, supportsInline: true),
            new EngineInputSpec("tag", ValueType.Tag, required: true, supportsInline: true),
            new EngineInputSpec("price", ValueType.Price, required: true, supportsInline: true)
        });

    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } =
        Array.AsReadOnly(new[] { ActionEvaluationMode.OnEvent, ActionEvaluationMode.OnCandleClose });

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Action;
    }
}