using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Сравнивает два числовых ряда выбранной операцией и выдаёт Condition.
///     Преобразует числовые расчёты в условия для логических нод и фильтрации событийных флагов.
/// </summary>
public sealed class CompareNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Compare;

    public NodeCategory Category => NodeCategory.Logic;

    public int SchemaVersion => 2;

    public IReadOnlyList<PortSpec> Inputs { get; } = [new PortSpec("a", PortKind.Series, "a"), new PortSpec("b", PortKind.Series, "b")];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Condition, "out", ValueType.Boolean)];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [new ParamSpec("op", "Operation", ParamValueKind.Text, ">", AllowedValues: ["<", "<=", "=", "!=", ">=", ">"])];

    public bool IsPureComputation => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return inherited;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return EngineValue.FromBoolean(EngineOperators.Compare(context.Inputs["a"], context.Inputs["b"], context.Parameters.TextSettings["op"]));
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        ValueType? a = input("a");
        ValueType? b = input("b");

        if (a.HasValue && b.HasValue && (a != b || (!UnitAlgebra.IsNumeric(a.Value) &&
                !(a is ValueType.OrderSide or ValueType.OrderType or ValueType.RejectionReason &&
                    NodeTextSettingResolver.Resolve(node, Params, "op") is "=" or "!="))))
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatibleUnits, $"Incompatible operands: {a} and {b}.", node.Id, "b"));
        }

        return null;
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        double a = inputs["a"].SeriesValue;
        double b = inputs["b"].SeriesValue;

        return NodeOutput.Condition(
            parameters.GetString("op") switch
            {
                "<" => a < b,
                "<=" => a <= b,
                "=" => a == b,
                "!=" => a != b,
                ">=" => a >= b,
                ">" => a > b,
                _ => throw new InvalidOperationException("Unknown comparison.")
            });
    }
}