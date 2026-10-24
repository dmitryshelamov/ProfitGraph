using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     State projection inputs and output storage owned by one replay execution.
/// </summary>
public readonly record struct NodeTradingEvaluationContext(
    CompiledTradingNode Node,
    ResolvedEngineParameters Parameters,
    DispatchEvaluationContext Dispatch,
    Dictionary<string, EngineValue> Values,
    OrderSizingService Sizing)
{
    public void Number(string key, decimal value)
    {
        Values[key] = EngineValue.Numeric(Node.OutputTypes[key], value);
    }
}