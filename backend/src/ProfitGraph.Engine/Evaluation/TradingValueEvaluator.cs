using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Pure account-state and preview projection against a single immutable dispatch snapshot.
/// </summary>
public sealed class TradingValueEvaluator
{
    private readonly OrderSizingService _sizing = new OrderSizingService();

    internal void EvaluateForReplay(
        CompiledTradingNode node,
        ResolvedEngineParameters parameters,
        DispatchEvaluationContext context,
        Dictionary<string, EngineValue> values,
        EngineValue[] outputs)
    {
        foreach ((PortReference port, ValueType type) in node.OutputBindings)
        {
            values[port.PortKey] = EngineValue.Unavailable(type);
        }

        node.Definition.EvaluateTrading(new NodeTradingEvaluationContext(node, parameters, context, values, _sizing));

        for (var index = 0; index < node.OutputBindings.Length; index++)
        {
            string key = node.OutputBindings[index].Key.PortKey;
            int slot = node.OutputSlotsByKey[key];
            EngineValue value = values[key];

            if (value.ValueType != node.OutputTypes[key])
            {
                throw new InvalidOperationException("Runtime output does not match its compiled units.");
            }

            outputs[slot] = value;
        }
    }
}