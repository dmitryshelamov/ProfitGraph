using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Выполняет выбранную арифметическую операцию над двумя Series-входами.
///     Поддерживает сложение, вычитание, умножение, деление, минимум, максимум и остаток от деления; деление на ноль отклоняется.
/// </summary>
public sealed class ArithmeticNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Arithmetic;

    public NodeCategory Category => NodeCategory.Logic;

    public int SchemaVersion => 2;

    public IReadOnlyList<PortSpec> Inputs { get; } = [new PortSpec("a", PortKind.Series, "a"), new PortSpec("b", PortKind.Series, "b")];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Series, "out")];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [new ParamSpec("op", "Operation", ParamValueKind.Text, "Add", AllowedValues: ["Add", "Subtract", "Multiply", "Divide", "Min", "Max", "Modulo"])];

    public bool IsPureComputation => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return inherited;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return EngineOperators.Arithmetic(context.Node.FirstOutputType, context.Inputs["a"].Number, context.Inputs["b"].Number, context.Parameters.TextSettings["op"]);
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        ValueType? a = input("a");
        ValueType? b = input("b");
        ValueType? result = a.HasValue && b.HasValue ? UnitAlgebra.Resolve(NodeTextSettingResolver.Resolve(node, Params, "op"), a.Value, b.Value) : null;

        if (a.HasValue && b.HasValue && result is null)
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatibleUnits, $"Incompatible operands: {a} and {b}.", node.Id, "b"));
        }

        return result;
    }

    public (decimal Min, decimal Max)? ResolveStaticBounds(
        GraphNode node,
        Func<ParamValue, (decimal Min, decimal Max)?> bound,
        Func<string, (decimal Min, decimal Max)?> inputBound)
    {
        (decimal Min, decimal Max)? a = inputBound("a");
        (decimal Min, decimal Max)? b = inputBound("b");
        string operation = node.Parameters.GetValueOrDefault("op") is FixedParamValue { Value: TextParamLiteral text } ? text.Value : "Add";

        return (a, b, operation) switch
        {
            ({ } x, { } y, "Add") => (x.Min + y.Min, x.Max + y.Max),
            ({ } x, { } y, "Subtract") => (x.Min - y.Max, x.Max - y.Min),
            ({ } x, { } y, "Min") => (decimal.Min(x.Min, y.Min), decimal.Min(x.Max, y.Max)),
            ({ } x, { } y, "Max") => (decimal.Max(x.Min, y.Min), decimal.Max(x.Max, y.Max)),
            ({ } x, { } y, "Multiply") => (new[] { x.Min * y.Min, x.Min * y.Max, x.Max * y.Min, x.Max * y.Max }.Min(),
                new[] { x.Min * y.Min, x.Min * y.Max, x.Max * y.Min, x.Max * y.Max }.Max()),
            _ => null
        };
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        double a = inputs["a"].SeriesValue;
        double b = inputs["b"].SeriesValue;

        return NodeOutput.Series(
            parameters.GetString("op") switch
            {
                "Add" => a + b,
                "Subtract" => a - b,
                "Multiply" => a * b,
                "Divide" when b != 0 => a / b,
                "Divide" => throw new InvalidOperationException("Division by zero."),
                "Min" => Math.Min(a, b),
                "Max" => Math.Max(a, b),
                "Modulo" when b != 0 => a % b,
                "Modulo" => throw new InvalidOperationException("Division by zero."),
                _ => throw new InvalidOperationException("Unknown arithmetic operation.")
            });
    }
}