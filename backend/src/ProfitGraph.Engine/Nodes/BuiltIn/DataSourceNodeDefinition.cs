using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Исходная нода графа без входов: выдаёт цену закрытия текущей свечи через единственный Series-выход.
///     Даёт Phase 0 начальную точку для проверки каталога и вычислений нод.
/// </summary>
/// <remarks>
///     Свеча берётся из NodeContext; параметры источника описывают настройки и сами не загружают рыночные данные.
///     Площадка не является параметром графа: её выбирает прогон или бот (ADR 0009).
/// </remarks>
public sealed class DataSourceNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.DataSource;

    public NodeCategory Category => NodeCategory.Data;

    public int SchemaVersion => 3;

    public IReadOnlyList<PortSpec> Inputs => Array.Empty<PortSpec>();

    public IReadOnlyList<PortSpec> Outputs { get; } =
        [new PortSpec("out", PortKind.Series, "OHLCV", ValueType.Price)];

    public IReadOnlyList<ParamSpec> Params { get; } =
    [
        new ParamSpec("symbol", "Symbol", ParamValueKind.Text, "BTC/USDT"),
        new ParamSpec("timeframe", "Timeframe", ParamValueKind.Text, "1h")
    ];

    public bool IsCandleClocked => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.CandleClocked;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return EngineValue.Numeric(context.Node.FirstOutputType, context.Candle!.Close);
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
        return new Dictionary<string, NodeOutput>
        {
            ["out"] = NodeOutput.Series((double)context.CurrentCandle.Close)
        };
    }
}