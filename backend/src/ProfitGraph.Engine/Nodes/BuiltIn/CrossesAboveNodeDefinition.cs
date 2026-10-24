using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Определяет пересечение ряда A выше ряда B по текущему и предыдущему образцам.
///     Выдаёт Condition только на баре пересечения; требует доступных предыдущих значений обоих рядов.
/// </summary>
public sealed class CrossesAboveNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.CrossesAbove;

    public NodeCategory Category => NodeCategory.Signal;

    public int SchemaVersion => 2;

    public IReadOnlyList<PortSpec> Inputs { get; } = [new PortSpec("a", PortKind.Series, "a"), new PortSpec("b", PortKind.Series, "b")];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Condition, "out", ValueType.Boolean)];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [];

    public bool IsCandleClocked => true;

    public bool IsIndicator => true;

    public IReadOnlyList<string> SampleInputKeys { get; } = ["a", "b"];

    public bool EvaluatesUnavailableInputs => true;

    public int WarmupOffset => 1;

    public bool IsCrossover => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.CandleClocked;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return CandleIndicators.Crossover(context.IndicatorState, context.Inputs["a"], context.Inputs["b"], above: true);
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        ValueType? a = input("a");
        ValueType? b = input("b");

        if (a.HasValue && b.HasValue && (a != b || !UnitAlgebra.IsNumeric(a.Value)))
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
        IReadOnlyList<NodeOutput> a = context.InputHistory("a");
        IReadOnlyList<NodeOutput> b = context.InputHistory("b");

        return NodeOutput.Condition(
            a.Count >= 2 && b.Count >= 2 && a[^2].IsAvailable && b[^2].IsAvailable &&
            a[^2].SeriesValue <= b[^2].SeriesValue && a[^1].SeriesValue > b[^1].SeriesValue);
    }
}