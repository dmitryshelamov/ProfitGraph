using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     One trade between given prices (ADR 0060): a limit buy at <c>entryPrice</c> on start, a limit sale of what it bought at
///     <c>targetPrice</c>, and a market sale of the position when a closed candle's close is at or below <c>stopPrice</c>. It never
///     buys again in the same run, so a scanner trade card runs it once per bot.
/// </summary>
public static class LevelTradeStrategyBlockFactory
{
    public const string EntryTag = "level.entry";
    public const string TargetTag = "level.target";
    public const string StopTag = "level.stop";

    public static StrategyDefinition Create(decimal buyAmount = 100m, decimal entryPrice = 100m, decimal stopPrice = 95m, decimal targetPrice = 110m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
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
                        new StrategyCheck("NoActiveOrders"), Flat(Position("quantity")), Compare(">=", Balance("quoteAvailable"), Parameter("buyAmount")),
                        Preview("Buy", "Limit", Parameter("buyAmount"), Parameter("entryPrice"))
                    ]),
                new StrategyBlock(
                    "entry",
                    "order",
                    "Лимитная покупка на входе",
                    orderKind: "Limit",
                    side: "Buy",
                    tag: Tag(EntryTag),
                    amount: Parameter("buyAmount"),
                    price: Parameter("entryPrice")),

                // A cancelled entry may have bought part of its amount, which gets the target too; after a stop the close is at or below it.
                new StrategyBlock(
                    "targetConditions",
                    "condition",
                    "Цель можно выставить?",
                    join: "all",
                    checks:
                    [
                        NoTag(TargetTag), NoTag(StopTag), Compare(">", Value("close"), Parameter("stopPrice")),
                        Preview("Sell", "Limit", Balance("baseAvailable"), Parameter("targetPrice"))
                    ]),
                new StrategyBlock(
                    "target",
                    "order",
                    "Лимитная продажа на цели",
                    orderKind: "Limit",
                    side: "Sell",
                    tag: Tag(TargetTag),
                    quantity: Balance("baseAvailable"),
                    price: Parameter("targetPrice")),
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock("stopPositionCheck", "condition", "Позиция закрыта?", join: "all", checks: [Flat(Position("quantity"))]),
                new StrategyBlock(
                    "stopConditions",
                    "condition",
                    "Сработал стоп?",
                    join: "all",
                    checks: [NoTag(StopTag), Compare("<=", Value("close"), Parameter("stopPrice"))]),
                new StrategyBlock("stopCancelEntry", "cancel", "Отмена входа", tag: Tag(EntryTag)),
                new StrategyBlock("stopCancelTarget", "cancel", "Отмена цели", tag: Tag(TargetTag)),
                new StrategyBlock("stop", "order", "Стоп по рынку", orderKind: "Market", side: "Sell", tag: Tag(StopTag), quantity: Balance("baseAvailable"))
            ],
            [
                Wire("start", "out", "entryConditions"), Wire("entryConditions", "yes", "entry"),
                Wire("entry", "filled", "targetConditions"), Wire("entry", "cancelled", "targetConditions"), Wire("targetConditions", "yes", "target"),
                Wire("clock", "out", "stopPositionCheck"), Wire("stopPositionCheck", "no", "stopConditions"),
                Wire("stopConditions", "yes", "stopCancelEntry"), Wire("stopCancelEntry", "completed", "stopCancelTarget"),
                Wire("stopCancelTarget", "completed", "stop")
            ],
            StrategyTemplateKind.LevelTrade);
    }
}