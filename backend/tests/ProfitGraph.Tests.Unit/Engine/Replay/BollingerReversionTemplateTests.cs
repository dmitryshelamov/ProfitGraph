using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class BollingerReversionTemplateTests
{
    // Bands of period 3 warm up on three candles.
    [Test]
    public void BuysBelowTheLowerBandAndSellsAtTheMiddle()
    {
        // Range 1 closes at 94 under the lower band 95.17 (middle 98, σ 2.83): the entry fills at range 2 open (94). Range 2 closes
        // at 96 under the middle 96.67; range 3 closes at 99 over the middle 96.33: the exit fills at range 4 open (99).
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            BollingerReversionStrategyBlockFactory.Create(bandPeriod: 3, bandWidth: 1m),
            [100m, 100m, 100m, 100m, 94m, 96m, 99m, 100m]);
        fills.Length.ShouldBe(2);
        fills[0].ShouldBeFill(OrderSide.Buy, "bollinger.entry", candleIndex: 2, price: 94m);
        fills[1].ShouldBeFill(OrderSide.Sell, "bollinger.exit", candleIndex: 4, price: 99m);
    }

    [Test]
    public void StopSellsBeforeTheMiddleIsReached()
    {
        // The entry fills at range 2 open (94), so the stop is 89.3. Range 2 closes at 90 under the lower band while in position;
        // range 3 closes at 89 under the middle 91: only the stop fires.
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            BollingerReversionStrategyBlockFactory.Create(bandPeriod: 3, bandWidth: 1m),
            [100m, 100m, 100m, 100m, 94m, 90m, 89m, 88m]);
        fills.Length.ShouldBe(2);
        fills[0].ShouldBeFill(OrderSide.Buy, "bollinger.entry", candleIndex: 2, price: 94m);
        fills[1].ShouldBeFill(OrderSide.Sell, "bollinger.exit", candleIndex: 4, price: 89m);
    }

    [Test]
    public void CloseInsideWiderBandsDoesNotBuy()
    {
        // With two σ the lower band of the 94 close is 92.34.
        BacktestLedgerEntry[] fills = IndicatorTemplateReplay.Fills(
            BollingerReversionStrategyBlockFactory.Create(bandPeriod: 3, bandWidth: 2m),
            [100m, 100m, 100m, 100m, 94m, 96m, 99m, 100m]);
        fills.ShouldBeEmpty();
    }
}