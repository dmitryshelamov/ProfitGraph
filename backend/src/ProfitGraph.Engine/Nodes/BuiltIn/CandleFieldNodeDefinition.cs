using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Nodes.BuiltIn;

/// <summary>
///     Источник выбранного поля текущей закрытой свечи: Open, High, Low, Close или Volume.
///     Позволяет подключать разные поля OHLCV к индикаторам и расчётам.
/// </summary>
public sealed class CandleFieldNodeDefinition : INodeDefinition
{
    public string TypeKey => NodeTypeKeys.CandleField;

    public NodeCategory Category => NodeCategory.Data;

    public int SchemaVersion => 2;

    public IReadOnlyList<PortSpec> Inputs { get; } = [];

    public IReadOnlyList<PortSpec> Outputs { get; } = [new PortSpec("out", PortKind.Series, "out")];

    public IReadOnlyList<ParamSpec> Params { get; } =
        [new ParamSpec("field", "Field", ParamValueKind.Text, "Close", AllowedValues: ["Open", "High", "Low", "Close", "Volume"])];

    public bool IsCandleClocked => true;

    public NodeClock ResolveClock(NodeClock inherited)
    {
        return NodeClock.CandleClocked;
    }

    public EngineValue EvaluateEngine(NodeEngineEvaluationContext context)
    {
        return EngineValue.Numeric(
            context.Node.FirstOutputType,
            context.Parameters.TextSettings["field"] switch
            {
                "Open" => context.Candle!.Open, "High" => context.Candle!.High, "Low" => context.Candle!.Low,
                "Volume" => context.Candle!.Volume, _ => context.Candle!.Close
            });
    }

    public ValueType? ResolveOutputType(GraphNode node, Func<string, ValueType?> input, List<GraphDiagnostic> diagnostics)
    {
        return NodeTextSettingResolver.Resolve(node, Params, "field") == "Volume" ? ValueType.BaseAmount : ValueType.Price;
    }

    public IReadOnlyDictionary<string, NodeOutput> Evaluate(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return new Dictionary<string, NodeOutput> { ["out"] = EvaluateValue(context, inputs, parameters) };
    }

    private static NodeOutput EvaluateValue(NodeContext context, IReadOnlyDictionary<string, NodeOutput> inputs, ResolvedParams parameters)
    {
        return NodeOutput.Series(
            parameters.GetString("field") switch
            {
                "Open" => (double)context.CurrentCandle.Open,
                "High" => (double)context.CurrentCandle.High,
                "Low" => (double)context.CurrentCandle.Low,
                "Close" => (double)context.CurrentCandle.Close,
                "Volume" => (double)context.CurrentCandle.Volume,
                _ => throw new InvalidOperationException("Unknown candle field.")
            });
    }
}