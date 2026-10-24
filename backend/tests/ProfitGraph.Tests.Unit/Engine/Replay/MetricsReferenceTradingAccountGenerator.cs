using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

internal static class MetricsReferenceTradingAccountGenerator
{
    internal static TradingAccount Generate(decimal startingCapital = 100m, decimal feeRate = 0m)
    {
        var settings = new ExecutionSettings(startingCapital, slippage: 0m, feeRate, feeRate);
        var rules = new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2);

        return new TradingAccount(settings, rules);
    }
}