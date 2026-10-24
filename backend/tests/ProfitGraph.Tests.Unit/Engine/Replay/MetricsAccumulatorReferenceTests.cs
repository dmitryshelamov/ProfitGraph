using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

[TestFixture]
internal sealed class MetricsAccumulatorReferenceTests
{
    private static void Execute(TradingAccount account, OrderSide side, decimal amount, decimal price, int candleIndex)
    {
        var request = new OrderRequest(side, OrderType.Market, amount, LimitPrice: null, Tag: null, "metrics-reference", candleIndex);
        OrderSubmission submission = account.Submit(request);
        submission.Rejection.ShouldBeNull();
        Order order = submission.Order.ShouldNotBeNull();
        OrderExecution execution = account.Execute(order.Id, price);
        execution.Rejection.ShouldBeNull();
        execution.Fill.ShouldNotBeNull();
    }

    private static void AssertSegment(
        SampleMetrics segment,
        decimal total,
        decimal realized,
        decimal unrealized,
        decimal returnPercent,
        decimal feesBase,
        decimal feesQuote,
        decimal drawdown,
        long executions)
    {
        AssertDecimal(segment.TotalPnl, total);
        AssertDecimal(segment.RealizedPnl, realized);
        AssertDecimal(segment.UnrealizedPnl, unrealized);
        AssertDecimal(segment.ReturnPercent, returnPercent);
        AssertDecimal(segment.FeesBase, feesBase);
        AssertDecimal(segment.FeesQuote, feesQuote);
        AssertDecimal(segment.MaxDrawdownPercent, drawdown);
        segment.Executions.ShouldBe(executions);
        AssertDecimal(segment.TotalPnl, realized + unrealized);
    }

    private static void AssertDecimal(decimal? actual, decimal expected)
    {
        actual.ShouldNotBeNull();
        actual.Value.ShouldBe(expected);
    }

    [Test]
    public void BothFeeCurrenciesAreSegmentedAndAlreadyIncludedInContinuousAccountPnl()
    {
        TradingAccount account = MetricsReferenceTradingAccountGenerator.Generate(feeRate: 0.1m);
        var initial = new MetricsAccumulator(startingCapital: 100m, OutOfSampleSplit.Create(fraction: 0.5m).Split(barCount: 4));

        Execute(account, OrderSide.Buy, amount: 100m, price: 50m, candleIndex: 0);
        MetricsAccumulator first = initial.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 0, open: 50m, close: 60m), newExecutions: 1);

        Execute(account, OrderSide.Sell, amount: 0.9m, price: 70m, candleIndex: 1);
        MetricsAccumulator boundary = first.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 1, open: 70m, close: 80m), newExecutions: 1);

        Execute(account, OrderSide.Buy, amount: 56.7m, price: 63m, candleIndex: 2);
        MetricsAccumulator third = boundary.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 2, open: 63m, close: 100m), newExecutions: 1);

        Execute(account, OrderSide.Sell, amount: 1.71m, price: 100m, candleIndex: 3);
        MetricsAccumulator final = third.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 3, open: 100m, close: 100m), newExecutions: 1);
        BacktestMetricsSnapshot metrics = final.Snapshot;

        AssertDecimal(first.Snapshot.Full.EndEquity, expected: 108m);
        AssertDecimal(boundary.Snapshot.Full.EndEquity, expected: 128.7m);
        AssertDecimal(third.Snapshot.Full.EndEquity, expected: 171m);
        AssertDecimal(metrics.Full.EndEquity, expected: 153.9m);
        AssertSegment(
            metrics.Full,
            total: 53.9m,
            realized: 53.9m,
            unrealized: 0m,
            returnPercent: 53.9m,
            feesBase: 0.29m,
            feesQuote: 23.4m,
            drawdown: 10m,
            executions: 4);
        AssertSegment(
            metrics.InSample,
            total: 28.7m,
            realized: 6.7m,
            unrealized: 22m,
            returnPercent: 28.7m,
            feesBase: 0.2m,
            feesQuote: 6.3m,
            drawdown: 0m,
            executions: 2);
        SampleMetrics holdout = metrics.OutOfSample.ShouldNotBeNull();
        AssertSegment(
            holdout,
            total: 25.2m,
            realized: 47.2m,
            unrealized: -22m,
            returnPercent: 25.2m,
            feesBase: 0.09m,
            feesQuote: 17.1m,
            drawdown: 10m,
            executions: 2);
        AssertDecimal(holdout.BoundaryEquity, expected: 128.7m);
        AssertDecimal(metrics.Full.TotalPnl, metrics.InSample.TotalPnl!.Value + holdout.TotalPnl!.Value);
        AssertDecimal(metrics.Full.FeesBase, metrics.InSample.FeesBase!.Value + holdout.FeesBase!.Value);
        AssertDecimal(metrics.Full.FeesQuote, metrics.InSample.FeesQuote!.Value + holdout.FeesQuote!.Value);
        metrics.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void ContinuousHoldoutUsesBoundaryDeltasAndRunCapitalWithIndependentDrawdownPeaks()
    {
        TradingAccount account = MetricsReferenceTradingAccountGenerator.Generate();
        var initial = new MetricsAccumulator(startingCapital: 100m, OutOfSampleSplit.Create(fraction: 0.5m).Split(barCount: 4));

        Execute(account, OrderSide.Buy, amount: 100m, price: 100m, candleIndex: 0);
        MetricsAccumulator first = initial.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 0, open: 100m, close: 120m), newExecutions: 1);

        Execute(account, OrderSide.Sell, amount: 1m, price: 90m, candleIndex: 1);
        MetricsAccumulator boundary = first.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 1, open: 90m, close: 90m), newExecutions: 1);

        Execute(account, OrderSide.Buy, amount: 90m, price: 90m, candleIndex: 2);
        MetricsAccumulator third = boundary.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 2, open: 90m, close: 110m), newExecutions: 1);

        Execute(account, OrderSide.Sell, amount: 1m, price: 130m, candleIndex: 3);
        MetricsAccumulator final = third.Advance(MetricsReferenceCandleSnapshotGenerator.Generate(account, candleIndex: 3, open: 130m, close: 130m), newExecutions: 1);
        BacktestMetricsSnapshot metrics = final.Snapshot;

        initial.ObservedCandles.ShouldBe(expected: 0);
        first.ObservedCandles.ShouldBe(expected: 1);
        boundary.ObservedCandles.ShouldBe(expected: 2);
        final.ObservedCandles.ShouldBe(expected: 4);
        AssertDecimal(first.Snapshot.Full.EndEquity, expected: 120m);
        AssertDecimal(boundary.Snapshot.Full.EndEquity, expected: 90m);
        AssertDecimal(third.Snapshot.Full.EndEquity, expected: 110m);
        AssertDecimal(metrics.Full.EndEquity, expected: 130m);
        AssertSegment(metrics.Full, total: 30m, realized: 30m, unrealized: 0m, returnPercent: 30m, feesBase: 0m, feesQuote: 0m, drawdown: 25m, executions: 4);
        AssertSegment(metrics.InSample, total: -10m, realized: -10m, unrealized: 0m, returnPercent: -10m, feesBase: 0m, feesQuote: 0m, drawdown: 25m, executions: 2);
        SampleMetrics holdout = metrics.OutOfSample.ShouldNotBeNull();
        AssertSegment(holdout, total: 40m, realized: 40m, unrealized: 0m, returnPercent: 40m, feesBase: 0m, feesQuote: 0m, drawdown: 0m, executions: 2);
        AssertDecimal(holdout.BoundaryEquity, expected: 90m);
        AssertDecimal(metrics.Full.TotalPnl, metrics.InSample.TotalPnl!.Value + holdout.TotalPnl!.Value);
        metrics.Diagnostics.ShouldBeEmpty();
    }
}