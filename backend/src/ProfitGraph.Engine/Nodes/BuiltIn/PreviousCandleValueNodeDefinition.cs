using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Выдаёт предыдущий образец подключённого числового ряда из истории запуска.
///     Нужен для сравнения соседних баров; при отсутствии истории значение недоступно.
/// </summary>
public sealed class PreviousCandleValueNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.PreviousCandleValue;

    public NodeCategory Category => NodeCategory.Indicator;

    public int SchemaVersion => 2;

    public IReadOnlyList<PortSpec> Inputs { get; } = [new PortSpec("in", PortKind.Series, "in")];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Series, "out")];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [];

    public bool IsCandleClocked => true;

    public bool IsIndicator => true;

    public IReadOnlyList<string> SampleInputKeys { get; } = ["in"];

    public bool EvaluatesUnavailableInputs => true;

    public int WarmupOffset => 1;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.CandleClocked;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return CandleIndicators.Previous(context.IndicatorState, context.Inputs["in"], context.Node.FirstOutputType);
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        ValueType? sample = input("in");

        if (sample.HasValue && !UnitAlgebra.IsNumeric(sample.Value))
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatibleUnits, $"A numeric series is required, got {sample}.", node.Id, "in"));

            return null;
        }

        return sample;
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        IReadOnlyList<NodeOutput> history = context.InputHistory("in");

        return history.Count >= 2 ? history[^2] : NodeOutput.Unavailable(PortKind.Series);
    }
}