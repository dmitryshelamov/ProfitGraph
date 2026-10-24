using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Источник событийного флага на каждом баре вычислительного интерпретатора.
///     Представляет такт закрытой свечи; пока не создаёт события с идентификатором или payload.
/// </summary>
public sealed class OnCandleClosedNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.OnCandleClosed;

    public NodeCategory Category => NodeCategory.Signal;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = [];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Event, "out", ValueType.Event, EventPayloadType.Candle)];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [];

    public bool EvaluatesDispatch => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.Event;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return context.IsCandleClose ? EngineValue.FromEvent(context.Occurrence!) : EngineValue.Unavailable(ValueType.Event);
    }
}