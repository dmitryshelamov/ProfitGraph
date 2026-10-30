using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     The costs of a scanner trade (ADR 0065): limit fees on the entry and the target, a market fee and the spread on the stop.
/// </summary>
public sealed class ScanTradeCostsTests
{
    private static readonly ScanTradeCosts Costs = new ScanTradeCosts(limitFeeRate: 0.001m, marketFeeRate: 0.0018m, spread: 0.0002m);

    [Test]
    public void AStopPaysTheEntryLimitFeeTheMarketFeeAndTheSpread()
    {
        Costs.AtStop.ShouldBe(0.003m);
        Costs.Loss(amount: 100m, entry: 100m, stop: 95m).ShouldBe(5.3m);
    }

    [Test]
    public void ATargetPaysALimitFeeOnEachSide()
    {
        Costs.AtTarget.ShouldBe(0.002m);
        Costs.Profit(amount: 100m, entry: 100m, target: 110m).ShouldBe(9.8m);

        // A short gains when the price falls to its target.
        Costs.Profit(amount: 100m, entry: 100m, target: 90m).ShouldBe(9.8m);

        // A move smaller than the fees loses.
        Costs.Profit(amount: 100m, entry: 100m, target: 100.1m).ShouldBe(-0.1m);
    }

    [Test]
    public void AStopNearerThanWhatItPaysIsTooTight()
    {
        // The production trade of 2026-10-30: entry 0.356, stop 0.35535, 0.18 % away against 0.3 % of costs.
        Costs.TooTight(entry: 0.356m, stop: 0.35535m).ShouldBeTrue();
        Costs.TooTight(entry: 100m, stop: 99.7m).ShouldBeFalse();
        Costs.TooTight(entry: 100m, stop: 100.2m).ShouldBeTrue();
    }

    [Test]
    public void FlatCostsHaveOneFeeAndNoSpread()
    {
        ScanTradeCosts.Flat(feeRate: 0.001m).ShouldBe(new ScanTradeCosts(limitFeeRate: 0.001m, marketFeeRate: 0.001m, spread: 0m));
        Should.Throw<ArgumentException>(() => new ScanTradeCosts(limitFeeRate: -0.001m, marketFeeRate: 0m, spread: 0m));
    }
}