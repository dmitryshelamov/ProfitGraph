using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata for the Grid node (ADR 0058): an event starts the grid; its levels live in the engine, not in the graph.
/// </summary>
public sealed class GridNodeDefinition : INodeDefinition
{
    public static IReadOnlyList<(string Key, ValueType Type)> Settings { get; } = Array.AsReadOnly(
        new[]
        {
            ("levels", ValueType.Integer), ("step", ValueType.Scalar), ("profitStep", ValueType.Scalar), ("lot", ValueType.QuoteAmount),
            ("maxOpenBuys", ValueType.Integer), ("shiftThreshold", ValueType.Scalar), ("anchorCeiling", ValueType.Scalar),
            ("stopBelow", ValueType.Scalar)
        });

    public string TypeKey => nameof(TradingNodeBehavior.Grid);
    public bool IsAction => true;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.Grid;
    public NodeClock? EngineClock => NodeClock.Action;
    public NodeCategory Category => NodeCategory.Signal;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[] { new PortSpec("trigger", PortKind.Event, "trigger", ValueType.Event) }
            .Concat(Settings.Select(setting => new PortSpec(setting.Key, PortKind.Series, setting.Key, setting.Type)))
            .ToArray());

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("started", PortKind.Event, "Started", ValueType.Event, EventPayloadType.Grid),
            new PortSpec("stopped", PortKind.Event, "Stopped", ValueType.Event, EventPayloadType.Grid)
        });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.Empty<ParamSpec>();

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.AsReadOnly(
        Settings.Select(setting => new EngineInputSpec(setting.Key, setting.Type, required: true, supportsInline: true)).ToArray());

    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.AsReadOnly(new[] { ActionEvaluationMode.OnEvent });

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Action;
    }
}