using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Inputs for one decimal node evaluation; mutable indicator state belongs to the evaluator run.
/// </summary>
public readonly record struct NodeEngineEvaluationContext(
    CompiledTradingNode Node,
    IReadOnlyDictionary<string, EngineValue> Inputs,
    ResolvedEngineParameters Parameters,
    Candle? Candle,
    Candle? PreviousCandle,
    EventOccurrence? Occurrence,
    bool IsCandleClose,
    CandleIndicatorState? State)
{
    public CandleIndicatorState IndicatorState => State ?? throw new InvalidOperationException("This node has no indicator state.");
}