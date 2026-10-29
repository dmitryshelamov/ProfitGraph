using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class MacdTrendTemplateTests
{
    // MACD(1, 2, 2) warms up on slow + signal − 1 = 3 candles and the cross on one more; EMA(3) of the trend needs three.
    [Test]
    public void BuysOnTheCrossAboveTheSignalOverTheTrendAndSellsOnTheCrossBelow()
    {
        // Range 2 closes at 100: MACD 1.63 crosses over its signal 0.74 (from −1.11 under −1.04) above EMA(3) 98, so the entry
        // fills at range 3 open (100). Range 4 closes at 102: MACD −0.04 crosses under 0.47, so the exit fills at range 5 open (102).
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            MacdTrendStrategyBlockFactory.Create(macdFast: 1, macdSlow: 2, macdSignal: 2, trendPeriod: 3),
            [100m, 100m, 100m, 100m, 96m, 94m, 100m, 104m, 102m, 98m]);
        fills.Length.ShouldBe(2);
        fills[0].ShouldBeFill(OrderSide.Buy, "macd.entry", candleIndex: 3, price: 100m);
        fills[1].ShouldBeFill(OrderSide.Sell, "macd.exit", candleIndex: 5, price: 102m);
    }

    [Test]
    public void CrossBelowTheTrendDoesNotBuy()
    {
        // Range 2 closes at 92: MACD −0.15 crosses over its signal −0.74, but the close is under EMA(3) 93.
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            MacdTrendStrategyBlockFactory.Create(macdFast: 1, macdSlow: 2, macdSignal: 2, trendPeriod: 3),
            [100m, 100m, 100m, 100m, 96m, 90m, 92m, 94m, 90m]);
        fills.ShouldBeEmpty();
    }
}