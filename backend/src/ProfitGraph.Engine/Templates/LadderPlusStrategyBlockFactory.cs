using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Profit Fox Ladder with a close-evaluated stop below the average cost and a realized-profit target that blocks new cycles.
/// </summary>
public static class LadderPlusStrategyBlockFactory
{
    public static StrategyDefinition Create(
        decimal buyAmount = 100m,
        decimal allocationCap = 300m,
        decimal drop = 0.1m,
        decimal rise = 0.1m,
        decimal stopLoss = 0.2m,
        decimal profitTarget = 1000m)
    {
        StrategyDefinition original = ProfitFoxLadderStrategyBlockFactory.Create(buyAmount, allocationCap, drop, rise);
        StrategyBlock[] blocks = original.Blocks.Select(block => block.Id == "entryConditions" ? WithProfitTarget(block) : block).ToArray();

        return new StrategyDefinition(
            [
                .. original.Parameters,
                new StrategyParameter("stopLoss", ValueType.Scalar, stopLoss, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("profitTarget", ValueType.QuoteAmount, profitTarget, StrategyParameterDisplayUnit.Quote)
            ],
            [
                .. original.Values,
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue(
                    "stopPrice",
                    Arithmetic("Multiply", Position("averageCost"), Arithmetic("Subtract", Constant(number: 1m, ValueType.Scalar), Parameter("stopLoss"))))
            ],
            [
                .. blocks,
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),

                // The exit order reserves the base, so a sell preview cannot tell a stoppable position from dust; the flat check can.
                new StrategyBlock("stopPositionCheck", "condition", "Позиция закрыта?", join: "all", checks: [Flat(Position("quantity"))]),
                new StrategyBlock(
                    "stopConditions",
                    "condition",
                    "Сработал стоп?",
                    join: "all",
                    checks: [NoTag("ladder.stop"), NoTag("ladder.entry"), Compare("<=", Value("close"), Value("stopPrice"))]),
                new StrategyBlock("stopCancelAverage", "cancel", "Отмена усреднения", tag: Tag("ladder.average")),
                new StrategyBlock("stopCancelExit", "cancel", "Отмена выхода", tag: Tag("ladder.exit")),
                new StrategyBlock("stop", "order", "Стоп по рынку", orderKind: "Market", side: "Sell", tag: Tag("ladder.stop"), quantity: Balance("baseAvailable"))
            ],
            [
                .. original.Wires,
                Wire("clock", "out", "stopPositionCheck"), Wire("stopPositionCheck", "no", "stopConditions"),
                Wire("stopConditions", "yes", "stopCancelAverage"),
                Wire("stopCancelAverage", "completed", "stopCancelExit"), Wire("stopCancelExit", "completed", "stop"),
                Wire("stop", "filled", "cancelAverage")
            ],
            StrategyTemplateKind.LadderPlus);
    }

    private static StrategyBlock WithProfitTarget(StrategyBlock block)
    {
        return new StrategyBlock(
            block.Id,
            block.Kind,
            block.DisplayName,
            block.Source,
            block.Join,
            [.. block.Checks, Compare("<", Position("realizedPnl"), Parameter("profitTarget"))],
            block.OrderKind,
            block.Side,
            block.Tag,
            block.Amount,
            block.Quantity,
            block.Price,
            block.SizeMode,
            block.MinimumOrderPolicy);
    }
}