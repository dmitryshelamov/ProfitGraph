using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Объединяет два событийных флага логическим ИЛИ в один Event-выход.
///     В текущей фазе не сохраняет отдельные возникновения событий и их причинный контекст.
/// </summary>
public sealed class MergeEventsNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.MergeEvents;

    public NodeCategory Category => NodeCategory.Signal;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = [new PortSpec("a", PortKind.Event, "a", ValueType.Event), new PortSpec("b", PortKind.Event, "b", ValueType.Event)];

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
        EngineValue result = context.Inputs.Values.FirstOrDefault(value => value.IsAvailable);

        return result.IsAvailable ? result : EngineValue.Unavailable(ValueType.Event);
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return NodeOutput.Event(inputs["a"].EventFired || inputs["b"].EventFired);
    }
}