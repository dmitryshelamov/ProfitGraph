using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     One short trade between given prices (ADR 0061), the mirror of <see cref="LevelTradeStrategyBlockFactory" />: a limit sale of
///     <c>sellAmount / entryPrice</c> at <c>entryPrice</c> on start, a limit buy of what it owes at <c>targetPrice</c>, and a market buy
///     of the short when a closed candle's close is at or above <c>stopPrice</c>. It never sells again in the same run.
/// </summary>
public static class LevelTradeShortStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal sellAmount = 100m, decimal entryPrice = 100m, decimal stopPrice = 105m, decimal targetPrice = 90m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("sellAmount", ValueType.QuoteAmount, sellAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("entryPrice", ValueType.Price, entryPrice, StrategyParameterDisplayUnit.None),
                new StrategyParameter("stopPrice", ValueType.Price, stopPrice, StrategyParameterDisplayUnit.None),
                new StrategyParameter("targetPrice", ValueType.Price, targetPrice, StrategyParameterDisplayUnit.None)
            ],
            [new StrategyValue("close", new ValueExpression("candle", field: "Close"))],
            [
                new StrategyBlock("start", "event", "Начало прогона", "OnStart"),
                new StrategyBlock(
                    "entryConditions",
                    "condition",
                    "Можно войти?",
                    join: "all",
                    checks:
                    [
                        new StrategyCheck("NoActiveOrders"), Flat(Position("quantity")),
                        new StrategyCheck(
                            "OrderSizePreviewValid",
                            operands: new Dictionary<string, ValueExpression> { ["amount"] = Parameter("sellAmount"), ["price"] = Parameter("entryPrice") },
                            side: "Sell",
                            orderKind: "Limit",
                            sizeMode: "amount")
                    ]),
                new StrategyBlock(
                    "entry",
                    "order",
                    "Лимитная продажа на входе",
                    orderKind: "Limit",
                    side: "Sell",
                    tag: Tag(LevelTradeStrategyBlockFactory.EntryTag),
                    amount: Parameter("sellAmount"),
                    price: Parameter("entryPrice"),
                    sizeMode: "amount"),

                // A cancelled entry may have sold part of its amount, which gets the target too; after a stop the close is at or above it.
                new StrategyBlock(
                    "targetConditions",
                    "condition",
                    "Цель можно выставить?",
                    join: "all",
                    checks:
                    [
                        NoTag(LevelTradeStrategyBlockFactory.TargetTag), NoTag(LevelTradeStrategyBlockFactory.StopTag),
                        Compare("<", Value("close"), Parameter("stopPrice")), Cover("Limit", Parameter("targetPrice"))
                    ]),
                new StrategyBlock(
                    "target",
                    "order",
                    "Лимитная покупка на цели",
                    orderKind: "Limit",
                    side: "Buy",
                    tag: Tag(LevelTradeStrategyBlockFactory.TargetTag),
                    quantity: Balance("baseAvailable"),
                    price: Parameter("targetPrice"),
                    sizeMode: "quantity"),
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock("stopPositionCheck", "condition", "Позиция закрыта?", join: "all", checks: [Flat(Position("quantity"))]),
                new StrategyBlock(
                    "stopConditions",
                    "condition",
                    "Сработал стоп?",
                    join: "all",
                    checks: [NoTag(LevelTradeStrategyBlockFactory.StopTag), Compare(">=", Value("close"), Parameter("stopPrice"))]),
                new StrategyBlock("stopCancelEntry", "cancel", "Отмена входа", tag: Tag(LevelTradeStrategyBlockFactory.EntryTag)),
                new StrategyBlock("stopCancelTarget", "cancel", "Отмена цели", tag: Tag(LevelTradeStrategyBlockFactory.TargetTag)),
                new StrategyBlock(
                    "stop",
                    "order",
                    "Стоп по рынку",
                    orderKind: "Market",
                    side: "Buy",
                    tag: Tag(LevelTradeStrategyBlockFactory.StopTag),
                    quantity: Balance("baseAvailable"),
                    sizeMode: "quantity")
            ],
            [
                Wire("start", "out", "entryConditions"), Wire("entryConditions", "yes", "entry"),
                Wire("entry", "filled", "targetConditions"), Wire("entry", "cancelled", "targetConditions"), Wire("targetConditions", "yes", "target"),
                Wire("clock", "out", "stopPositionCheck"), Wire("stopPositionCheck", "no", "stopConditions"),
                Wire("stopConditions", "yes", "stopCancelEntry"), Wire("stopCancelEntry", "completed", "stopCancelTarget"),
                Wire("stopCancelTarget", "completed", "stop")
            ],
            StrategyTemplateKind.LevelTradeShort,
            PositionDirection.Short);
    }

    private static StrategyCheck Cover(string kind, ValueExpression price)
    {
        return new StrategyCheck(
            "OrderSizePreviewValid",
            operands: new Dictionary<string, ValueExpression> { ["quantity"] = Balance("baseAvailable"), ["price"] = price },
            side: "Buy",
            orderKind: kind,
            sizeMode: "quantity");
    }
}