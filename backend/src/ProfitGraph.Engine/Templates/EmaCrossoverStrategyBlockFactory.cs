using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

public static class EmaCrossoverStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal buyAmount = 100m, int emaFast = 12, int emaSlow = 26, decimal stopLoss = 0.05m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("emaFast", ValueType.Integer, emaFast, StrategyParameterDisplayUnit.None),
                new StrategyParameter("emaSlow", ValueType.Integer, emaSlow, StrategyParameterDisplayUnit.None),
                new StrategyParameter("stopLoss", ValueType.Scalar, stopLoss, StrategyParameterDisplayUnit.Percent)
            ],
            [
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue("fastEma", Indicator("EMA", Value("close"), Parameter("emaFast"))),
                new StrategyValue("slowEma", Indicator("EMA", Value("close"), Parameter("emaSlow"))),
                new StrategyValue("stopPrice", StopPrice())
            ],
            [
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock(
                    "entryConditions",
                    "condition",
                    "Быстрая EMA пересекла медленную вверх?",
                    join: "all",
                    checks:
                    [
                        Cross("CrossesAbove", Value("fastEma"), Value("slowEma")), new StrategyCheck("PositionFlat"), new StrategyCheck("NoActiveOrders"),
                        Preview("Buy", "Market", Parameter("buyAmount"))
                    ]),
                new StrategyBlock(
                    "exitConditions",
                    "condition",
                    "Позицию можно продать?",
                    join: "all",
                    checks: [new StrategyCheck("NoActiveOrders"), Preview("Sell", "Market", Position("quantity"))]),
                new StrategyBlock(
                    "exitSignal",
                    "condition",
                    "Пересечение вниз или стоп?",
                    join: "any",
                    checks: [Cross("CrossesBelow", Value("fastEma"), Value("slowEma")), Compare("<=", Value("close"), Value("stopPrice"))]),
                new StrategyBlock("entry", "order", "Покупка по рынку", orderKind: "Market", side: "Buy", tag: Tag("ema.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock("exit", "order", "Продажа по рынку", orderKind: "Market", side: "Sell", tag: Tag("ema.exit"), quantity: Position("quantity"))
            ],
            [
                Wire("clock", "out", "entryConditions"), Wire("entryConditions", "yes", "entry"),
                Wire("clock", "out", "exitConditions"), Wire("exitConditions", "yes", "exitSignal"), Wire("exitSignal", "yes", "exit")
            ],
            StrategyTemplateKind.EmaCrossover);
    }
}