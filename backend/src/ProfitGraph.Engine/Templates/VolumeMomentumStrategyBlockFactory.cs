using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

public static class VolumeMomentumStrategyBlockFactory
{
    public static StrategyDefinition Create(
        decimal buyAmount = 100m,
        decimal impulse = 0.05m,
        decimal volumeMultiple = 2m,
        int volumePeriod = 50,
        decimal takeProfit = 0.1m,
        decimal stopLoss = 0.05m,
        int maxHold = 12)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("impulse", ValueType.Scalar, impulse, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("volumeMultiple", ValueType.Scalar, volumeMultiple, StrategyParameterDisplayUnit.None),
                new StrategyParameter("volumePeriod", ValueType.Integer, volumePeriod, StrategyParameterDisplayUnit.None),
                new StrategyParameter("takeProfit", ValueType.Scalar, takeProfit, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("stopLoss", ValueType.Scalar, stopLoss, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("maxHold", ValueType.Integer, maxHold, StrategyParameterDisplayUnit.None)
            ],
            [
                new StrategyValue("open", new ValueExpression("candle", field: "Open")),
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue("volume", new ValueExpression("candle", field: "Volume")),
                new StrategyValue(
                    "baseVolume",
                    new ValueExpression(
                        "previous",
                        arguments: new Dictionary<string, ValueExpression>
                        {
                            ["in"] = new ValueExpression(
                                "SMA",
                                arguments: new Dictionary<string, ValueExpression> { ["in"] = Value("volume"), ["period"] = Parameter("volumePeriod") })
                        })),
                new StrategyValue(
                    "impulsePrice",
                    Arithmetic("Multiply", Value("open"), Arithmetic("Add", Constant(number: 1m, ValueType.Scalar), Parameter("impulse")))),
                new StrategyValue("volumeFloor", Arithmetic("Multiply", Value("baseVolume"), Parameter("volumeMultiple"))),
                new StrategyValue(
                    "targetPrice",
                    Arithmetic("Multiply", Position("lastBuyPrice"), Arithmetic("Add", Constant(number: 1m, ValueType.Scalar), Parameter("takeProfit")))),
                new StrategyValue(
                    "stopPrice",
                    Arithmetic("Multiply", Position("lastBuyPrice"), Arithmetic("Subtract", Constant(number: 1m, ValueType.Scalar), Parameter("stopLoss"))))
            ],
            [
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock(
                    "entryConditions",
                    "condition",
                    "Есть импульс на объёме?",
                    join: "all",
                    checks:
                    [
                        new StrategyCheck("PositionFlat"), new StrategyCheck("NoActiveOrders"),
                        Compare(">=", Value("close"), Value("impulsePrice")), Compare(">=", Value("volume"), Value("volumeFloor")),
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
                    "Цель, стоп или срок?",
                    join: "any",
                    checks:
                    [
                        Compare(">=", Value("close"), Value("targetPrice")), Compare("<=", Value("close"), Value("stopPrice")),
                        Compare(">=", Position("barsHeld"), Parameter("maxHold"))
                    ]),
                new StrategyBlock("entry", "order", "Покупка по рынку", orderKind: "Market", side: "Buy", tag: Tag("momentum.entry"), amount: Parameter("buyAmount")),
                new StrategyBlock("exit", "order", "Продажа по рынку", orderKind: "Market", side: "Sell", tag: Tag("momentum.exit"), quantity: Position("quantity"))
            ],
            [
                Wire("clock", "out", "entryConditions"), Wire("entryConditions", "yes", "entry"),
                Wire("clock", "out", "exitConditions"), Wire("exitConditions", "yes", "exitSignal"), Wire("exitSignal", "yes", "exit")
            ],
            StrategyTemplateKind.VolumeMomentum);
    }
}