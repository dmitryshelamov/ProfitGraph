using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     ADR 0057: capital deployed and committed, time in position and time under water, per sample.
/// </summary>
public sealed class CapitalCostMetricsTests
{
    [Test]
    public void FullSample_ReportsTheLargestCapitalAndTheSharesOfClosesInPositionAndUnderWater()
    {
        SampleMetrics full = Accumulate(new SampleRanges(InSampleBars: 5, OutOfSampleBars: 0)).Snapshot.Full;

        full.MaxCapitalDeployed.ShouldBe(200m);
        full.MaxCapitalDeployedPercent.ShouldBe(20m);
        full.MaxCapitalCommitted.ShouldBe(260m);
        full.TimeInPositionPercent.ShouldBe(60m);
        full.TimeUnderWaterPercent.ShouldBe(40m);
        full.LongestUnderWaterCandles.ShouldBe(2);
    }

    [Test]
    public void Holdout_StartsItsOwnMaximaAndShares()
    {
        BacktestMetricsSnapshot snapshot = Accumulate(new SampleRanges(InSampleBars: 3, OutOfSampleBars: 2)).Snapshot;

        snapshot.InSample.MaxCapitalCommitted.ShouldBe(260m);
        snapshot.InSample.LongestUnderWaterCandles.ShouldBe(2);
        SampleMetrics holdout = snapshot.OutOfSample.ShouldNotBeNull();
        holdout.MaxCapitalDeployed.ShouldBe(200m);
        holdout.MaxCapitalCommitted.ShouldBe(200m);
        holdout.TimeInPositionPercent.ShouldBe(50m);
        holdout.TimeUnderWaterPercent.ShouldBe(0m);
        holdout.LongestUnderWaterCandles.ShouldBe(0);
    }

    [Test]
    public void DustWithALossIsNotUnderWater()
    {
        MetricsAccumulator accumulator = new MetricsAccumulator(startingCapital: 1000m, new SampleRanges(InSampleBars: 1, OutOfSampleBars: 0))
            .Advance(Close(sequence: 1, equity: 999m, unrealized: -0.01m, costBasis: 0.02m, reserved: 0m, inPosition: false), newExecutions: 0);

        accumulator.Snapshot.Full.TimeInPositionPercent.ShouldBe(0m);
        accumulator.Snapshot.Full.TimeUnderWaterPercent.ShouldBe(0m);
    }

    private static MetricsAccumulator Accumulate(SampleRanges ranges)
    {
        return new MetricsAccumulator(startingCapital: 1000m, ranges)
            .Advance(Close(sequence: 1, equity: 1000m, unrealized: 0m, costBasis: 0m, reserved: 50m, inPosition: false), newExecutions: 0)
            .Advance(Close(sequence: 2, equity: 995m, unrealized: -5m, costBasis: 100m, reserved: 50m, inPosition: true), newExecutions: 1)
            .Advance(Close(sequence: 3, equity: 990m, unrealized: -10m, costBasis: 200m, reserved: 60m, inPosition: true), newExecutions: 1)
            .Advance(Close(sequence: 4, equity: 1003m, unrealized: 3m, costBasis: 200m, reserved: 0m, inPosition: true), newExecutions: 0)
            .Advance(Close(sequence: 5, equity: 1010m, unrealized: 0m, costBasis: 0m, reserved: 0m, inPosition: false), newExecutions: 1);
    }

    private static CandleMetricObservation Close(long sequence, decimal equity, decimal unrealized, decimal costBasis, decimal reserved, bool inPosition)
    {
        return new CandleMetricObservation(
            sequence,
            (int)sequence - 1,
            StartingCapital: 1000m,
            equity,
            unrealized,
            RealizedPnl: 0m,
            FeesBase: 0m,
            FeesQuote: 0m,
            costBasis,
            reserved,
            inPosition);
    }
}