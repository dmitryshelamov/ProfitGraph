using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

public static class LadderStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal buyAmount = 100m, decimal allocationCap = 300m, decimal drop = 0.1m, decimal rise = 0.1m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("allocationCap", ValueType.QuoteAmount, allocationCap, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("drop", ValueType.Scalar, drop, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("rise", ValueType.Scalar, rise, StrategyParameterDisplayUnit.Percent)
            ],
            [
                new StrategyValue(
                    "nextPrice",
                    Arithmetic("Multiply", Position("lastBuyPrice"), Arithmetic("Subtract", Constant(number: 1m, ValueType.Scalar), Parameter("drop")))),
                new StrategyValue(
                    "exitPrice",
                    Arithmetic("Multiply", Position("averageCost"), Arithmetic("Add", Constant(number: 1m, ValueType.Scalar), Parameter("rise")))),
                new StrategyValue("remaining", Arithmetic("Subtract", Parameter("allocationCap"), Position("costBasis")))
            ],
            [
                new StrategyBlock("start", "event", "Начало прогона", "OnStart"),
                new StrategyBlock("restartEvent", "condition", "Позиция закрыта?", join: "all", checks: [Flat(Balance("baseAvailable"))]),
                new StrategyBlock("residualEvent", "condition", "Остаток можно продать?", join: "all", checks: [Preview("Sell", "Market", Balance("baseAvailable"))]),
                new StrategyBlock(
                    "entryConditions",
                    "condition",
                    "Можно войти?",
                    join: "all",
                    checks:
                    [NoTag("ladder.entry"), NoTag("ladder.average"), NoTag("ladder.exit"), Flat(Position("quantity")), Allocation(), Cash()]),
                new StrategyBlock(
                    "averageConditions",
                    "condition",
                    "Можно докупить?",
                    join: "all",
                    checks:
                    [NoTag("ladder.average"), NoTag("ladder.entry"), Allocation(), Cash(), Preview("Buy", "Limit", Parameter("buyAmount"), Value("nextPrice"))]),
                new StrategyBlock(
                    "exitConditions",
                    "condition",
                    "Выход можно выставить?",
                    join: "all",
                    checks:
                    [Preview("Sell", "Limit", Balance("baseAvailable"), Value("exitPrice"))]),
                new StrategyBlock("entry", "order", "Покупка по рынку", orderKind: "Market", side: "Buy", tag: Tag("ladder.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock(
                    "average",
                    "order",
                    "Лимитная покупка",
                    orderKind: "Limit",
                    side: "Buy",
                    tag: Tag("ladder.average"),
                    amount: Parameter("buyAmount"),
                    price: Value("nextPrice")),
                new StrategyBlock(
                    "exit",
                    "order",
                    "Лимитная продажа",
                    orderKind: "Limit",
                    side: "Sell",
                    tag: Tag("ladder.exit"),
                    quantity: Balance("baseAvailable"),
                    price: Value("exitPrice")),
                new StrategyBlock(
                    "residual",
                    "order",
                    "Продажа по рынку",
                    orderKind: "Market",
                    side: "Sell",
                    tag: Tag("ladder.exit"),
                    quantity: Balance("baseAvailable")),
                new StrategyBlock("cancelExit", "cancel", "Отмена ордера", tag: Tag("ladder.exit")),
                new StrategyBlock("cancelAverage", "cancel", "Отмена ордера", tag: Tag("ladder.average")),
                new StrategyBlock("protectiveCancel", "cancel", "Отмена ордера", tag: Tag("ladder.average"))
            ],
            [
                Wire("cancelAverage", "completed", "restartEvent"), Wire("cancelAverage", "completed", "residualEvent"),
                Wire("start", "out", "entryConditions"), Wire("restartEvent", "yes", "entryConditions"),
                Wire("entryConditions", "yes", "entry"), Wire("entry", "filled", "cancelExit"), Wire("average", "filled", "cancelExit"),
                Wire("cancelExit", "completed", "exitConditions"), Wire("exitConditions", "yes", "exit"),
                Wire("exit", "submitted", "averageConditions"), Wire("averageConditions", "yes", "average"),
                Wire("exit", "filled", "cancelAverage"), Wire("residual", "filled", "cancelAverage"), Wire("residualEvent", "yes", "residual"),
                Wire("exit", "rejected", "protectiveCancel"), Wire("residual", "rejected", "protectiveCancel")
            ],
            StrategyTemplateKind.Ladder);
    }

    private static StrategyCheck Allocation()
    {
        return Compare(">=", Value("remaining"), Parameter("buyAmount"));
    }

    private static StrategyCheck Cash()
    {
        return Compare(">=", Balance("quoteAvailable"), Parameter("buyAmount"));
    }
}