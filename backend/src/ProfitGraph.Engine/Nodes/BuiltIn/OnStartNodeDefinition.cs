using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Источник событийного флага только на первом переданном баре запуска.
///     В текущей фазе не учитывает торговый прогрев и не создаёт событие с идентификатором или payload.
/// </summary>
public sealed class OnStartNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.OnStart;

    public NodeCategory Category => NodeCategory.Signal;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = [];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Event, "out", ValueType.Event, EventPayloadType.Start)];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [];

    public bool EvaluatesDispatch => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Event;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return context.Occurrence?.Kind == EventKind.Start ? EngineValue.FromEvent(context.Occurrence) : EngineValue.Unavailable(ValueType.Event);
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return NodeOutput.Event(context.BarIndex == 0);
    }
}