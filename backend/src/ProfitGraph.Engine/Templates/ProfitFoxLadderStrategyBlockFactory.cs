using ProfitGraph.Engine.Blocks;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Resume-aware Ladder with explicit Spot minimum-notional raises bounded by remaining allocation.
/// </summary>
public static class ProfitFoxLadderStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal buyAmount = 100m, decimal allocationCap = 300m, decimal drop = 0.1m, decimal rise = 0.1m)
    {
        StrategyDefinition original = LadderResumeStrategyBlockFactory.Create(buyAmount, allocationCap, drop, rise);
        StrategyBlock[] blocks = original.Blocks.Select(WithPolicy).ToArray();

        return new StrategyDefinition(original.Parameters, original.Values, blocks, original.Wires, original.TemplateKind);
    }

    private static StrategyBlock WithPolicy(StrategyBlock block)
    {
        if (block.Id is not ("entry" or "average" or "entryConditions" or "averageConditions"))
        {
            return block;
        }

        IEnumerable<StrategyCheck> checks = block.Checks.Select(check => check.Type == "OrderSizePreviewValid" && check.Side == "Buy"
            ? RaisedPreview(check)
            : check);

        if (block.Id == "entryConditions")
        {
            checks = checks.Append(
                new StrategyCheck(
                    "OrderSizePreviewValid",
                    operands: new Dictionary<string, ValueExpression> { ["amount"] = Parameter("buyAmount"), ["quoteLimit"] = Value("remaining") },
                    side: "Buy",
                    orderKind: "Market",
                    minimumOrderPolicy: "RaiseToExchangeMinimum"));
        }

        return new StrategyBlock(
            block.Id,
            block.Kind,
            block.DisplayName,
            block.Source,
            block.Join,
            checks,
            block.OrderKind,
            block.Side,
            block.Tag,
            block.Amount,
            block.Quantity,
            block.Price,
            block.SizeMode,
            block.Kind == "order" ? "RaiseToExchangeMinimum" : null);
    }

    private static StrategyCheck RaisedPreview(StrategyCheck check)
    {
        var operands = new Dictionary<string, ValueExpression>(check.Operands) { ["quoteLimit"] = Value("remaining") };

        return new StrategyCheck(check.Type, check.Operation, operands, check.Tag, check.Side, check.OrderKind, check.SizeMode, "RaiseToExchangeMinimum");
    }
}