using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class EmaCrossoverTemplateTests
{
    // EMA(1) is the close; EMA(3) warms up on the first three closes and the cross needs one more, so four candles warm up.
    [Test]
    public void BuysOnTheCrossAboveAndSellsOnTheCrossBelow()
    {
        // Range 1 closes at 110 over EMA(3) 105 after 100 = 100: the entry fills at range 2 open (110). Range 3 closes at 106 under
        // EMA(3) 107.25 after 112 over 108.5: the exit fills at range 4 open (106), above the stop 104.5.
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            EmaCrossoverStrategyBlockFactory.Create(emaFast: 1, emaSlow: 3),
            [100m, 100m, 100m, 100m, 100m, 110m, 112m, 106m, 106m]);
        fills.Length.ShouldBe(2);
        fills[0].ShouldBeFill(OrderSide.Buy, "ema.entry", candleIndex: 2, price: 110m);
        fills[1].ShouldBeFill(OrderSide.Sell, "ema.exit", candleIndex: 4, price: 106m);
    }

    [Test]
    public void StopSellsWhileTheFastEmaIsStillAbove()
    {
        // The entry fills at a gap open of 120, so the stop is 114. Range 3 closes at 114, still over EMA(3) 113.25: only the stop
        // fires. The fast EMA stays above afterwards, so nothing buys again without a new cross.
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            EmaCrossoverStrategyBlockFactory.Create(emaFast: 1, emaSlow: 3),
            [100m, 100m, 100m, 100m, 100m, 110m, 120m, 114m, 114m, 115m],
            (Index: 6, Open: 120m));
        fills.Length.ShouldBe(2);
        fills[0].ShouldBeFill(OrderSide.Buy, "ema.entry", candleIndex: 2, price: 120m);
        fills[1].ShouldBeFill(OrderSide.Sell, "ema.exit", candleIndex: 4, price: 114m);
    }
}