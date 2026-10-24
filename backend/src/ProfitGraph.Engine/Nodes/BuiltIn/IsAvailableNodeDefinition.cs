using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Проверяет доступность числового входа, включая период прогрева индикатора.
///     Принимает недоступные значения и превращает их состояние в явный Condition.
/// </summary>
public sealed class IsAvailableNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.IsAvailable;

    public NodeCategory Category => NodeCategory.Logic;

    public int SchemaVersion => 2;

    public bool AcceptsUnavailableInputs => true;

    public IReadOnlyList<PortSpec> Inputs { get; } = [new PortSpec("in", PortKind.Series, "in")];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Condition, "out", ValueType.Boolean)];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [];

    public bool IsPureComputation => true;

    public bool EvaluatesUnavailableInputs => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return inherited;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return EngineValue.FromBoolean(context.Inputs["in"].IsAvailable);
    }
}