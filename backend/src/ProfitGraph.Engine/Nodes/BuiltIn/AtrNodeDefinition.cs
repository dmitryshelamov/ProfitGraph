using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Индикатор Phase 0, вычисляющий среднее True Range за period баров с учётом закрытия предыдущей свечи.
///     Проверяет работу LookBack на соседних свечах и ценовых разрывах; требует period + 1 свечу истории.
/// </summary>
/// <remarks>
///     Использует простое среднее True Range, без сглаживания Уайлдера.
///     Свечи берутся из NodeContext; ATR является источником индикатора OHLC, а не обработчиком произвольного Series.
/// </remarks>
public sealed class AtrNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.Atr;

    public NodeCategory Category => NodeCategory.Indicator;

    public int SchemaVersion => 3;

    public IReadOnlyList<PortSpec> Inputs { get; } = [];

    public IReadOnlyList<PortSpec> Outputs { get; } =
        [new PortSpec("out", PortKind.Series, NodeTypeKeys.Atr, ValueType.Price)];

    public IReadOnlyList<ParamSpec> Params { get; } =
    [
        new ParamSpec("period", "Period", ParamValueKind.Number, DefaultValue: 14, MinValue: 1, MaxValue: 200, Step: 1)
    ];

    public bool IsCandleClocked => true;

    public bool IsIndicator => true;

    public string WarmupPeriodKey => "period";

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.CandleClocked;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return CandleIndicators.Atr(
            context.IndicatorState,
            context.Candle!,
            context.PreviousCandle,
            checked((int)context.Parameters["period"].Number),
            context.Node.FirstOutputType);
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        return ValueType.Price;
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(
        NodeContext context,
        IReadOnlyDictionary<string, NodeOutput> inputs,
        ResolvedParams parameters)
    {
        int period = parameters.GetInt("period");

        if (context.BarIndex < period)
        {
            return new Dictionary<string, NodeOutput> { ["out"] = NodeOutput.Unavailable(PortKind.Series) };
        }

        IReadOnlyList<Candle> candles = context.LookBack(period + 1);
        IEnumerable<double> trueRanges = candles.Zip(
            candles.Skip(1),
            (previous, current) => Math.Max(
                (double)(current.High - current.Low),
                Math.Max((double)Math.Abs(current.High - previous.Close), (double)Math.Abs(current.Low - previous.Close))));
        double averageTrueRange = trueRanges.Average();

        return new Dictionary<string, NodeOutput> { ["out"] = NodeOutput.Series(averageTrueRange) };
    }
}