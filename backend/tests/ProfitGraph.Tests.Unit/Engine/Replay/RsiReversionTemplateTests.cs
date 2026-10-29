using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class RsiReversionTemplateTests
{
    // RSI(2) warms up on three candles and its previous value on one more.
    [Test]
    public void BuysWhenRsiReturnsAboveTheEntryLevelAndSellsAtTheExitLevel()
    {
        // RSI(2) runs 0, 0, 25, 62.5, 81.25 over closes 96, 92, 93, 95, 97. Range 3 lifts it over 30 from 25: the entry fills at
        // range 4 open (95). Range 4 reaches 81.25 >= 70: the exit fills at range 5 open (97).
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            RsiReversionStrategyBlockFactory.Create(rsiPeriod: 2),
            [100m, 100m, 100m, 100m, 96m, 92m, 93m, 95m, 97m, 98m]);
        fills.Length.ShouldBe(2);
        fills[0].ShouldBeFill(OrderSide.Buy, "rsi.entry", candleIndex: 4, price: 95m);
        fills[1].ShouldBeFill(OrderSide.Sell, "rsi.exit", candleIndex: 5, price: 97m);
    }

    [Test]
    public void StopSellsBelowTheEntry()
    {
        // The entry fills at 95, so the stop is 90.25; range 4 closes at 90 with RSI far from 70.
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            RsiReversionStrategyBlockFactory.Create(rsiPeriod: 2),
            [100m, 100m, 100m, 100m, 96m, 92m, 93m, 95m, 90m, 90m]);
        fills.Length.ShouldBe(2);
        fills[0].ShouldBeFill(OrderSide.Buy, "rsi.entry", candleIndex: 4, price: 95m);
        fills[1].ShouldBeFill(OrderSide.Sell, "rsi.exit", candleIndex: 5, price: 90m);
    }

    [Test]
    public void OversoldWithoutAReturnDoesNotBuy()
    {
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            RsiReversionStrategyBlockFactory.Create(rsiPeriod: 2),
            [100m, 100m, 100m, 100m, 96m, 92m, 90m, 88m, 86m, 85m]);
        fills.ShouldBeEmpty();
    }
}