using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

public static class BollingerReversionStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal buyAmount = 100m, int bandPeriod = 20, decimal bandWidth = 2m, decimal stopLoss = 0.05m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("bandPeriod", ValueType.Integer, bandPeriod, StrategyParameterDisplayUnit.None),
                new StrategyParameter("bandWidth", ValueType.Scalar, bandWidth, StrategyParameterDisplayUnit.None),
                new StrategyParameter("stopLoss", ValueType.Scalar, stopLoss, StrategyParameterDisplayUnit.Percent)
            ],
            [
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue("lowerBand", Band("lower")), new StrategyValue("middleBand", Band("middle")),
                new StrategyValue("stopPrice", StopPrice())
            ],
            [
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock(
                    "entryConditions",
                    "condition",
                    "Закрытие ниже нижней полосы?",
                    join: "all",
                    checks:
                    [
                        Compare("<", Value("close"), Value("lowerBand")), new StrategyCheck("PositionFlat"), new StrategyCheck("NoActiveOrders"),
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
                    "Возврат к средней или стоп?",
                    join: "any",
                    checks: [Compare(">=", Value("close"), Value("middleBand")), Compare("<=", Value("close"), Value("stopPrice"))]),
                new StrategyBlock("entry", "order", "Покупка по рынку", orderKind: "Market", side: "Buy", tag: Tag("bollinger.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock("exit", "order", "Продажа по рынку", orderKind: "Market", side: "Sell", tag: Tag("bollinger.exit"), quantity: Position("quantity"))
            ],
            [
                Wire("clock", "out", "entryConditions"), Wire("entryConditions", "yes", "entry"),
                Wire("clock", "out", "exitConditions"), Wire("exitConditions", "yes", "exitSignal"), Wire("exitSignal", "yes", "exit")
            ],
            StrategyTemplateKind.BollingerReversion);
    }

    private static ValueExpression Band(string field)
    {
        return new ValueExpression(
            "Bollinger",
            field: field,
            arguments: new Dictionary<string, ValueExpression>
            {
                ["in"] = Value("close"), ["period"] = Parameter("bandPeriod"), ["multiplier"] = Parameter("bandWidth")
            });
    }
}