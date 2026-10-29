using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

public static class MacdTrendStrategyBlockFactory
{
    public static StrategyDefinition Create(
        decimal buyAmount = 100m,
        int macdFast = 12,
        int macdSlow = 26,
        int macdSignal = 9,
        int trendPeriod = 200,
        decimal stopLoss = 0.05m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("macdFast", ValueType.Integer, macdFast, StrategyParameterDisplayUnit.None),
                new StrategyParameter("macdSlow", ValueType.Integer, macdSlow, StrategyParameterDisplayUnit.None),
                new StrategyParameter("macdSignal", ValueType.Integer, macdSignal, StrategyParameterDisplayUnit.None),
                new StrategyParameter("trendPeriod", ValueType.Integer, trendPeriod, StrategyParameterDisplayUnit.None),
                new StrategyParameter("stopLoss", ValueType.Scalar, stopLoss, StrategyParameterDisplayUnit.Percent)
            ],
            [
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue("macdLine", Macd("macd")), new StrategyValue("signalLine", Macd("signal")),
                new StrategyValue("trendEma", Indicator("EMA", Value("close"), Parameter("trendPeriod"))),
                new StrategyValue("stopPrice", StopPrice())
            ],
            [
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock(
                    "entryConditions",
                    "condition",
                    "MACD пересёк сигнальную вверх выше тренда?",
                    join: "all",
                    checks:
                    [
                        Cross("CrossesAbove", Value("macdLine"), Value("signalLine")), Compare(">", Value("close"), Value("trendEma")),
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
                    "MACD пересёк сигнальную вниз или стоп?",
                    join: "any",
                    checks: [Cross("CrossesBelow", Value("macdLine"), Value("signalLine")), Compare("<=", Value("close"), Value("stopPrice"))]),
                new StrategyBlock("entry", "order", "Покупка по рынку", orderKind: "Market", side: "Buy", tag: Tag("macd.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock("exit", "order", "Продажа по рынку", orderKind: "Market", side: "Sell", tag: Tag("macd.exit"), quantity: Position("quantity"))
            ],
            [
                Wire("clock", "out", "entryConditions"), Wire("entryConditions", "yes", "entry"),
                Wire("clock", "out", "exitConditions"), Wire("exitConditions", "yes", "exitSignal"), Wire("exitSignal", "yes", "exit")
            ],
            StrategyTemplateKind.MacdTrend);
    }

    private static ValueExpression Macd(string field)
    {
        return new ValueExpression(
            "MACD",
            field: field,
            arguments: new Dictionary<string, ValueExpression>
            {
                ["in"] = Value("close"), ["fastPeriod"] = Parameter("macdFast"), ["slowPeriod"] = Parameter("macdSlow"), ["signalPeriod"] = Parameter("macdSignal")
            });
    }
}