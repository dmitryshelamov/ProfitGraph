using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Индикатор среднего True Range за period баров с учётом закрытия предыдущей свечи; требует period + 1 свечу истории.
/// </summary>
/// <remarks>
///     Использует простое среднее True Range, без сглаживания Уайлдера.
///     Свечи берутся из истории контекста вычисления движка; ATR является источником индикатора OHLC, а не обработчиком произвольного Series.
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
}