using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Экспоненциальная скользящая средняя подключённого ряда с alpha = 2 / (period + 1); первое значение — простое среднее первых period отсчётов.
/// </summary>
/// <remarks>
///     Готова на period-й закрытой свече, как SMA. Вычисляется в decimal: EMA = (2 · x + (period − 1) · EMA') / (period + 1). Недоступный отсчёт
///     сбрасывает среднюю.
/// </remarks>
public sealed class EmaNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Ema;

    public NodeCategory Category => NodeCategory.Indicator;

    public int SchemaVersion => 1;

    public IReadOnlyList<PortSpec> Inputs { get; } =
        [new PortSpec("in", PortKind.Series, "Series")];

    public IReadOnlyList<PortSpec> Outputs { get; } =
        [new PortSpec("out", PortKind.Series, NodeTypeKeys.Ema)];

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
        return CandleIndicators.Ema(
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