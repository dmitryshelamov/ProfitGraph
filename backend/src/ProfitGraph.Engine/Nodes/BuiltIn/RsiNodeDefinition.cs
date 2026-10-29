using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Индекс относительной силы Уайлдера подключённого ряда: безразмерное значение от 0 до 100.
/// </summary>
/// <remarks>
///     Первые средние роста и падения — простые средние первых period изменений, поэтому значение появляется на (period + 1)-й закрытой свече, как
///     ATR. Затем A = (A' · (period − 1) + изменение) / period. Без падений RSI = 100, без роста — 0, без изменений — 50. Вычисляется в decimal;
///     недоступный отсчёт сбрасывает историю.
/// </remarks>
public sealed class RsiNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Rsi;

    public NodeCategory Category => NodeCategory.Indicator;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } =
        [new PortSpec("in", PortKind.Series, "Series")];

    public IReadOnlyList<PortSpec> Outputs { get; } =
        [new PortSpec("out", PortKind.Series, NodeTypeKeys.Rsi, ValueType.Scalar)];

    public IReadOnlyList<ParamSpec> Params { get; } =
    [
        new ParamSpec("period", "Period", ParamValueKind.Number, DefaultValue: 14, MinValue: 1, MaxValue: 200, Step: 1)
    ];

    public bool IsCandleClocked => true;

    public bool IsIndicator => true;

    public IReadOnlyList<string> SampleInputKeys { get; } = ["in"];

    public bool EvaluatesUnavailableInputs => true;

    public string WarmupPeriodKey => "period";

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.CandleClocked;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return CandleIndicators.Rsi(context.IndicatorState, context.Inputs["in"], checked((int)context.Parameters["period"].Number));
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        ValueType? sample = input("in");

        if (sample.HasValue && !UnitAlgebra.IsNumeric(sample.Value))
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.IncompatibleUnits, $"A numeric series is required, got {sample}.", node.Id, "in"));

            return null;
        }

        return ValueType.Scalar;
    }
}