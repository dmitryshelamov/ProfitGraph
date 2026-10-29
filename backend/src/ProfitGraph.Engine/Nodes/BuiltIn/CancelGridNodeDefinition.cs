using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Metadata for the CancelGrid node (ADR 0058, section 7): a cancel block that names a grid stops it without selling.
/// </summary>
public sealed class CancelGridNodeDefinition : INodeDefinition
{
    public string TypeKey => nameof(TradingNodeBehavior.CancelGrid);
    public bool IsAction => true;
    public TradingNodeBehavior? TradingBehavior => TradingNodeBehavior.CancelGrid;
    public NodeClock? EngineClock => NodeClock.Action;
    public NodeCategory Category => NodeCategory.Signal;
    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = Array.AsReadOnly(
        new[]
        {
            new PortSpec("trigger", PortKind.Event, "trigger", ValueType.Event),
            new PortSpec("enabled", PortKind.Condition, "enabled", ValueType.Boolean)
        });

    public IReadOnlyList<PortSpec> Outputs { get; } = Array.AsReadOnly(
        new[] { new PortSpec("completed", PortKind.Event, "Completed", ValueType.Event, EventPayloadType.CancelResult) });

    public IReadOnlyList<ParamSpec> Params { get; } = Array.AsReadOnly(new[] { new ParamSpec("grid", "grid", ParamValueKind.Text, string.Empty) });

    public IReadOnlyList<EngineInputSpec> EngineInputs { get; } = Array.AsReadOnly(
        new[] { new EngineInputSpec("enabled", ValueType.Boolean, required: false, supportsInline: true, EngineValue.FromBoolean(true)) });

    public IReadOnlyList<ActionEvaluationMode> AllowedEvaluationModes { get; } = Array.AsReadOnly(new[] { ActionEvaluationMode.OnEvent });

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Action;
    }
}