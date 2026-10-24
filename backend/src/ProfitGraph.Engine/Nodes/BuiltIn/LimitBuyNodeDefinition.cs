using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata and behavior for the LimitBuy node.
/// </summary>
public sealed class LimitBuyNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.LimitBuy);
    public bool IsAction => true;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.LimitBuy;
    public NodeClock? EngineClock => NodeClock.Action;
    public NodeCategory Category => NodeCategory.Signal;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("trigger", PortKind.Event, "trigger", ValueType.Event),
            new PortSpec("enabled", PortKind.Condition, "enabled", ValueType.Boolean),
            new PortSpec("amount", PortKind.Series, "amount", ValueType.QuoteAmount),
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

    public IReadOnlyList<ParamSpec> Params { get; } = Array.AsReadOnly(
        new[]
        {
            new ParamSpec(
                "minimumOrderPolicy",
                "minimumOrderPolicy",
                ParamValueKind.Text,
                "Reject",
                AllowedValues: Array.AsReadOnly(new[] { "Reject", "RaiseToExchangeMinimum" }))
        });

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.AsReadOnly(
        new[]
        {
            new EngineInputSpec("enabled", ValueType.Boolean, required: false, supportsInline: true, EngineValue.FromBoolean(true)),
            new EngineInputSpec("amount", ValueType.QuoteAmount, required: true, supportsInline: true),
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