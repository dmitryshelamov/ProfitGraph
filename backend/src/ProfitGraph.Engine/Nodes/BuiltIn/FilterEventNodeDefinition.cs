using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Пропускает входной событийный флаг только при истинном Condition.
///     Связывает вычислительные условия с событийными флагами, не выполняя торговых действий.
/// </summary>
public sealed class FilterEventNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.FilterEvent;

    public NodeCategory Category => NodeCategory.Signal;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } =
    [
        new PortSpec("event", PortKind.Event, "event", ValueType.Event),
        new PortSpec("condition", PortKind.Condition, "condition", ValueType.Boolean)
    ];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Event, "out", ValueType.Event)];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [];

    public bool EvaluatesDispatch => true;

    public bool EvaluatesUnavailableInputs => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Event;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return context.Inputs["event"].IsAvailable && context.Inputs["condition"].IsAvailable && context.Inputs["condition"].Boolean
            ? context.Inputs["event"]
            : EngineValue.Unavailable(ValueType.Event);
    }
}