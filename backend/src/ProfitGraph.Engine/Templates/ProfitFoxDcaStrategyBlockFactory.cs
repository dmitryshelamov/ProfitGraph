using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Profit Fox DCA: a market buy at the start and after every interval of closed candles in the position, without exits,
///     while the position cost stays within the allocation cap.
/// </summary>
public static class ProfitFoxDcaStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal buyAmount = 100m, decimal allocationCap = 1000m, int interval = 1)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("buyAmount", ValueType.QuoteAmount, buyAmount, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("allocationCap", ValueType.QuoteAmount, allocationCap, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("interval", ValueType.Integer, interval, StrategyParameterDisplayUnit.None)
            ],
            [
                new StrategyValue("remaining", Arithmetic("Subtract", Parameter("allocationCap"), Position("costBasis"))),
                new StrategyValue("intervalPhase", Arithmetic("Modulo", Position("barsHeld"), Parameter("interval")))
            ],
            [
                new StrategyBlock("start", "event", "Начало прогона", "OnStart"),
                new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock("flatCheck", "condition", "Позиции нет?", join: "all", checks: [new StrategyCheck("PositionFlat")]),
                new StrategyBlock(
                    "scheduleCheck",
                    "condition",
                    "Пора докупить?",
                    join: "all",
                    checks: [Compare("=", Value("intervalPhase"), Constant(number: 0m, ValueType.Integer))]),
                new StrategyBlock(
                    "buyConditions",
                    "condition",
                    "Можно купить?",
                    join: "all",
                    checks:
                    [
                        new StrategyCheck("NoActiveOrders"), Compare(">=", Value("remaining"), Parameter("buyAmount")),
                        Compare(">=", Balance("quoteAvailable"), Parameter("buyAmount")),
                        new StrategyCheck(
                            "OrderSizePreviewValid",
                            operands: new Dictionary<string, ValueExpression> { ["amount"] = Parameter("buyAmount"), ["quoteLimit"] = Value("remaining") },
                            side: "Buy",
                            orderKind: "Market",
                            minimumOrderPolicy: "RaiseToExchangeMinimum")
                    ]),
                new StrategyBlock(
                    "buy",
                    "order",
                    "Покупка по рынку",
                    orderKind: "Market",
                    side: "Buy",
                    tag: Tag("dca.buy"),
                    amount: Parameter("buyAmount"),
                    minimumOrderPolicy: "RaiseToExchangeMinimum")
            ],
            [
                Wire("start", "out", "buyConditions"), Wire("clock", "out", "flatCheck"), Wire("clock", "out", "scheduleCheck"),
                Wire("flatCheck", "yes", "buyConditions"), Wire("scheduleCheck", "yes", "buyConditions"), Wire("buyConditions", "yes", "buy")
            ],
            StrategyTemplateKind.DcaProfitFox);
    }
}