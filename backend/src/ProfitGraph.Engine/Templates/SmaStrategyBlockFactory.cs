using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

public static class SmaStrategyBlockFactory
{
    public static StrategyDefinition Create(int fastPeriod = 20, int slowPeriod = 50, decimal buyAmount = 100m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("fastPeriod", ValueType.Integer, fastPeriod, StrategyParameterDisplayUnit.None),
                new StrategyParameter("slowPeriod", ValueType.Integer, slowPeriod, StrategyParameterDisplayUnit.None),
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote)
            ],
            [
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue("fastSma", Sma("fastPeriod")), new StrategyValue("slowSma", Sma("slowPeriod"))
            ],
            [
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock(
                    "buySignal",
                    "condition",
                    "Сигнал на покупку",
                    join: "all",
                    checks:
                    [Cross("CrossesAbove"), new StrategyCheck("PositionFlat"), new StrategyCheck("NoActiveOrders")]),
                new StrategyBlock(
                    "sellSignal",
                    "condition",
                    "Сигнал на продажу",
                    join: "all",
                    checks:
                    [Cross("CrossesBelow"), new StrategyCheck("HasBaseBalance"), new StrategyCheck("NoActiveOrders")]),
                new StrategyBlock("buy", "order", "Покупка по рынку", orderKind: "Market", side: "Buy", tag: Tag("sma.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock("sell", "order", "Продажа по рынку", orderKind: "Market", side: "Sell", tag: Tag("sma.exit"), quantity: Balance("baseAvailable"))
            ],
            [Wire("clock", "out", "buySignal"), Wire("clock", "out", "sellSignal"), Wire("buySignal", "yes", "buy"), Wire("sellSignal", "yes", "sell")],
            StrategyTemplateKind.SmaCrossover);
    }

    private static ValueExpression Sma(string period)
    {
        return new ValueExpression("SMA", arguments: new Dictionary<string, ValueExpression> { ["in"] = Value("close"), ["period"] = Parameter(period) });
    }

    private static StrategyCheck Cross(string type)
    {
        return new StrategyCheck(type, operands: new Dictionary<string, ValueExpression> { ["a"] = Value("fastSma"), ["b"] = Value("slowSma") });
    }
}