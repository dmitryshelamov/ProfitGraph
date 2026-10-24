using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

[TestFixture]
internal sealed class MetricsValueStateTests
{
    private static void AssertSnapshotBits(BacktestMetricsSnapshot actual, BacktestMetricsSnapshot expected)
    {
        actual.Diagnostics.ShouldBe(expected.Diagnostics);
        AssertSegmentBits(actual.Full, expected.Full);
        AssertSegmentBits(actual.InSample, expected.InSample);
        AssertSegmentBits(actual.OutOfSample.ShouldNotBeNull(), expected.OutOfSample.ShouldNotBeNull());
    }

    private static void AssertSegmentBits(SampleMetrics actual, SampleMetrics expected)
    {
        actual.ShouldBe(expected);
        decimal?[] actualValues =
        [
            actual.BoundaryEquity, actual.EndEquity, actual.TotalPnl, actual.RealizedPnl, actual.UnrealizedPnl, actual.ReturnPercent,
            actual.FeesBase, actual.FeesQuote, actual.MaxDrawdownPercent
        ];
        decimal?[] expectedValues =
        [
            expected.BoundaryEquity, expected.EndEquity, expected.TotalPnl, expected.RealizedPnl, expected.UnrealizedPnl, expected.ReturnPercent,
            expected.FeesBase, expected.FeesQuote, expected.MaxDrawdownPercent
        ];

        for (var index = 0; index < actualValues.Length; index++)
        {
            actualValues[index].HasValue.ShouldBe(expectedValues[index].HasValue);

            if (actualValues[index] is decimal value)
            {
                decimal.GetBits(value).ShouldBe(decimal.GetBits(expectedValues[index]!.Value));
            }
        }
    }

    [Test]
    public void ObserverKeepsPreviouslyReturnedSnapshotsImmutableAcrossHoldout()
    {
        var ranges = new SampleRanges(InSampleBars: 2, OutOfSampleBars: 2);
        var observer = new MetricsObserver(startingCapital: 1000m, ranges);
        var initial = new MetricsAccumulator(startingCapital: 1000m, ranges);
        MetricsAccumulator accumulator = initial;
        BacktestMetricsSnapshot empty = observer.Snapshot;
        CandleMetricObservation[] observations =
        [
            new CandleMetricObservation(
                Sequence: 1,
                CandleIndex: 0,
                StartingCapital: 1000m,
                Equity: 1001.000m,
                UnrealizedPnl: 1.100m,
                RealizedPnl: -0.100m,
                FeesBase: 0.00010m,
                FeesQuote: 0.1000m),
            new CandleMetricObservation(
                Sequence: 2,
                CandleIndex: 1,
                StartingCapital: 1000m,
                Equity: 950.00m,
                UnrealizedPnl: -48.000m,
                RealizedPnl: -2.000m,
                FeesBase: 0.00020m,
                FeesQuote: 0.2000m),
            new CandleMetricObservation(
                Sequence: 3,
                CandleIndex: 2,
                StartingCapital: 1000m,
                Equity: 960.000m,
                UnrealizedPnl: -35.000m,
                RealizedPnl: -5.000m,
                FeesBase: 0.00030m,
                FeesQuote: 0.3000m),
            new CandleMetricObservation(
                Sequence: 4,
                CandleIndex: 3,
                StartingCapital: 1000m,
                Equity: 940.00m,
                UnrealizedPnl: -10.000m,
                RealizedPnl: -50.000m,
                FeesBase: 0.00040m,
                FeesQuote: 0.4000m)
        ];
        var snapshots = new List<BacktestMetricsSnapshot>();
        var expected = new List<BacktestMetricsSnapshot>();

        foreach (CandleMetricObservation observation in observations)
        {
            observer.OnCandleMetrics(observation);
            accumulator = accumulator.Advance(observation, newExecutions: 0);
            snapshots.Add(observer.Snapshot);
            expected.Add(accumulator.Snapshot);
            observer.Snapshot.ShouldBeSameAs(snapshots[^1]);
        }

        empty.Full.ObservedCandles.ShouldBe(expected: 0);
        initial.ObservedCandles.ShouldBe(expected: 0);

        for (var index = 0; index < snapshots.Count; index++)
        {
            AssertSnapshotBits(snapshots[index], expected[index]);
            snapshots[index].Full.ObservedCandles.ShouldBe(index + 1);
        }
    }

    [Test]
    public void ObserverPreservesDecimalOverflowDiagnosticsAndRejectedObservationState()
    {
        const decimal capital = 0.0000000000000000000000000001m;
        var ranges = new SampleRanges(InSampleBars: 1, OutOfSampleBars: 1);
        var observer = new MetricsObserver(capital, ranges);
        var accumulator = new MetricsAccumulator(capital, ranges);
        var first = new CandleMetricObservation(
            Sequence: 1,
            CandleIndex: 0,
            capital,
            Equity: 60000000000000000000000000000m,
            UnrealizedPnl: 59999999999999999999999999999m,
            RealizedPnl: 0m,
            FeesBase: 0m,
            FeesQuote: 0m);
        var second = new CandleMetricObservation(
            Sequence: 2,
            CandleIndex: 1,
            capital,
            Equity: 1m,
            UnrealizedPnl: -59999999999999999999999999999m,
            RealizedPnl: 59999999999999999999999999999m,
            FeesBase: 1.00m,
            FeesQuote: 2.000m);
        observer.OnCandleMetrics(first);
        accumulator = accumulator.Advance(first, newExecutions: 0);
        BacktestMetricsSnapshot previous = observer.Snapshot;
        Should.Throw<ArgumentException>(() => observer.OnCandleMetrics(first));
        observer.Snapshot.ShouldBeSameAs(previous);
        observer.OnCandleMetrics(second);
        accumulator = accumulator.Advance(second, newExecutions: 0);
        AssertSnapshotBits(observer.Snapshot, accumulator.Snapshot);
        observer.Snapshot.Full.ReturnPercent.ShouldBeNull();
        observer.Snapshot.OutOfSample.ShouldNotBeNull().UnrealizedPnl.ShouldBeNull();
        observer.Snapshot.Diagnostics.Count.ShouldBeGreaterThan(0);
        previous.Full.ObservedCandles.ShouldBe(expected: 1);
    }
}