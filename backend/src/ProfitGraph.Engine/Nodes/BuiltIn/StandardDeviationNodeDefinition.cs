using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Стандартное отклонение генеральной совокупности подключённого ряда за последние period отсчётов: sqrt(Σ(x − mean)² / period).
/// </summary>
/// <remarks>
///     Готово на period-й закрытой свече, как SMA; ровный ряд даёт ровно 0. Вычисляется в decimal с детерминированным корнем DecimalSquareRoot.
///     Недоступный отсчёт очищает окно.
/// </remarks>
public sealed class StandardDeviationNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.StandardDeviation;

    public NodeCategory Category => NodeCategory.Indicator;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } =
        [new PortSpec("in", PortKind.Series, "Series")];

    public IReadOnlyList<PortSpec> Outputs { get; } =
        [new PortSpec("out", PortKind.Series, NodeTypeKeys.StandardDeviation)];

    public IReadOnlyList<ParamSpec> Params { get; } =
    [
        new ParamSpec("period", "Period", ParamValueKind.Number, DefaultValue: 20, MinValue: 1, MaxValue: 200, Step: 1)
    ];

    public bool IsCandleClocked => true;

    public bool IsIndicator => true;

    public IReadOnlyList<string> SampleInputKeys { get; } = ["in"];

    public bool EvaluatesUnavailableInputs => true;

    public string WarmupPeriodKey => "period";

    public int WarmupOffset => -1;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.CandleClocked;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return CandleIndicators.StandardDeviation(
            context.IndicatorState,
            context.Inputs["in"],
            checked((int)context.Parameters["period"].Number),
            context.Node.FirstOutputType);
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        ValueType? sample = input("in");

        if (sample.HasValue && !UnitAlgebra.IsNumeric(sample.Value))
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatibleUnits, $"A numeric series is required, got {sample}.", node.Id, "in"));

            return null;
        }

        return sample == ValueType.Integer ? ValueType.Scalar : sample;
    }
}