using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

public static class RsiReversionStrategyBlockFactory
{
    public static StrategyDefinition Create(
        decimal buyAmount = 100m,
        int rsiPeriod = 14,
        decimal rsiEntry = 30m,
        decimal rsiExit = 70m,
        decimal stopLoss = 0.05m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("rsiPeriod", ValueType.Integer, rsiPeriod, StrategyParameterDisplayUnit.None),
                new StrategyParameter("rsiEntry", ValueType.Scalar, rsiEntry, StrategyParameterDisplayUnit.None),
                new StrategyParameter("rsiExit", ValueType.Scalar, rsiExit, StrategyParameterDisplayUnit.None),
                new StrategyParameter("stopLoss", ValueType.Scalar, stopLoss, StrategyParameterDisplayUnit.Percent)
            ],
            [
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue("rsi", Indicator("RSI", Value("close"), Parameter("rsiPeriod"))),
                new StrategyValue("previousRsi", Previous(Value("rsi"))),
                new StrategyValue("stopPrice", StopPrice())
            ],
            [
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock(
                    "entryConditions",
                    "condition",
                    "RSI вернулся выше уровня входа?",
                    join: "all",
                    checks:
                    [
                        Compare(">", Value("rsi"), Parameter("rsiEntry")), Compare("<=", Value("previousRsi"), Parameter("rsiEntry")),
                        new StrategyCheck("PositionFlat"), new StrategyCheck("NoActiveOrders"), Preview("Buy", "Market", Parameter("buyAmount"))
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
                    "RSI на уровне выхода или стоп?",
                    join: "any",
                    checks: [Compare(">=", Value("rsi"), Parameter("rsiExit")), Compare("<=", Value("close"), Value("stopPrice"))]),
                new StrategyBlock("entry", "order", "Покупка по рынку", orderKind: "Market", side: "Buy", tag: Tag("rsi.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock("exit", "order", "Продажа по рынку", orderKind: "Market", side: "Sell", tag: Tag("rsi.exit"), quantity: Position("quantity"))
            ],
            [
                Wire("clock", "out", "entryConditions"), Wire("entryConditions", "yes", "entry"),
                Wire("clock", "out", "exitConditions"), Wire("exitConditions", "yes", "exitSignal"), Wire("exitSignal", "yes", "exit")
            ],
            StrategyTemplateKind.RsiReversion);
    }
}