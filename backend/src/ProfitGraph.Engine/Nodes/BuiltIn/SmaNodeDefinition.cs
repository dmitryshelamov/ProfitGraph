using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Простой индикатор Phase 0 с одним Series-входом, одним выходом и числовым параметром period.
///     Вычисляет среднее цен закрытия за заданное окно, демонстрируя контракт ноды и работу с историей.
/// </summary>
/// <remarks>В графе усредняет историю подключённого ряда; при отдельном вызове без входов использует Close.</remarks>
public sealed class SmaNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Sma;

    public NodeCategory Category => NodeCategory.Indicator;

    public int SchemaVersion => 2;

    public IReadOnlyList<PortSpec> Inputs { get; } =
        [new PortSpec("in", PortKind.Series, "Series")];

    public IReadOnlyList<PortSpec> Outputs { get; } =
        [new PortSpec("out", PortKind.Series, NodeTypeKeys.Sma)];

    public IReadOnlyList<ParamSpec> Params { get; } =
    [
        new ParamSpec("period", "Period", ParamValueKind.Number, DefaultValue: 14, MinValue: 1, MaxValue: 200, Step: 1)
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
        return CandleIndicators.Sma(
            context.IndicatorState,
            context.Inputs["in"],
            checked((int)context.Parameters["period"].Number),
            context.Node.FirstOutputType,
            context.Candle!.OpenTime.UtcDateTime);
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

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(
        NodeContext context,
        IReadOnlyDictionary<string, NodeOutput> inputs,
        ResolvedParams parameters)
    {
        int period = parameters.GetInt("period");

        if (inputs.ContainsKey("in"))
        {
            NodeOutput[] window = context.InputHistory("in").TakeLast(period).ToArray();
            NodeOutput result = window.Length == period && window.All(value => value.IsAvailable)
                ? NodeOutput.Series(window.Average(value => value.SeriesValue))
                : NodeOutput.Unavailable(PortKind.Series);

            return new Dictionary<string, NodeOutput> { ["out"] = result };
        }

        IReadOnlyList<Candle> history = context.LookBack(period);
        double average = history.Average(candle => (double)candle.Close);

        return new Dictionary<string, NodeOutput> { ["out"] = NodeOutput.Series(average) };
    }
}