using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Инвертирует входное логическое условие и выдаёт Condition.
///     Позволяет выражать отрицание без изменения ноды-источника.
/// </summary>
public sealed class NotNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Not;

    public NodeCategory Category => NodeCategory.Logic;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } = [new PortSpec("in", PortKind.Condition, "in", ValueType.Boolean)];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Condition, "out", ValueType.Boolean)];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [];

    public bool IsPureComputation => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return inherited;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return EngineValue.FromBoolean(!context.Inputs["in"].Boolean);
    }
}