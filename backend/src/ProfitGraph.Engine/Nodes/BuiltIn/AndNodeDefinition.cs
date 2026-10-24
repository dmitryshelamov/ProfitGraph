using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Выдаёт истинный Condition только когда оба входных условия истинны.
///     Нужен для объединения обязательных условий графа.
/// </summary>
public sealed class AndNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.And;

    public NodeCategory Category => NodeCategory.Logic;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } =
        [new PortSpec("a", PortKind.Condition, "a", ValueType.Boolean), new PortSpec("b", PortKind.Condition, "b", ValueType.Boolean)];

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
        return EngineValue.FromBoolean(context.Inputs["a"].Boolean && context.Inputs["b"].Boolean);
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return NodeOutput.Condition(inputs["a"].ConditionValue && inputs["b"].ConditionValue);
    }
}