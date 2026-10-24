using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Источник числового значения из уже разрешённого параметра value.
///     Позволяет использовать литерал, выбранное значение Optimize или Wired-привязку как Series-выход.
/// </summary>
public sealed class ConstantNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Constant;

    public NodeCategory Category => NodeCategory.Data;

    public int SchemaVersion => 2;

    public IReadOnlyList<PortSpec> Inputs { get; } = [];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Series, "out")];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [new ParamSpec("value", "Value", ParamValueKind.Number, DefaultValue: 0d)];

    public bool IsPureComputation => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return inherited;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return context.Parameters["value"];
    }

    public ValueType ResolveParameterType(GraphNode node, ParamSpec parameter)
    {
        return parameter.Key == "value" ? node.DeclaredValueType ?? ValueType.Scalar : ValueType.Tag;
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        return node.DeclaredValueType ?? ValueType.Scalar;
    }

    public (decimal Min, decimal Max)? ResolveStaticBounds(
        GraphNode node,
        Func<ParamValue, (decimal Min, decimal Max)?> bound,
        Func<string, (decimal Min, decimal Max)?> inputBound)
    {
        return node.Parameters.TryGetValue("value", out ParamValue? value) ? bound(value) : (0m, 0m);
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return NodeOutput.Series(parameters.GetNumber("value"));
    }
}