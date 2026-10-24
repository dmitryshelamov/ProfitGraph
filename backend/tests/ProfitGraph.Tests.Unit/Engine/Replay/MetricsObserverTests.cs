using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

[TestFixture]
internal sealed class MetricsObserverTests
{
    [TestCase(arg1: false, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    [TestCase(arg1: true, arg2: false)]
    [TestCase(arg1: true, arg2: true)]
    public void ScalarMetricsExactlyMatchFullSnapshotsWithFeesAndContinuousHoldout(bool ladder, bool holdout)
    {
        StrategyGraph graph = ladder ? LadderStrategyGraphGenerator.Generate() : SmaStrategyGraphGenerator.Generate();
        INodeDefinition[] definitions = EngineGraphFixture.Definitions
            .Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior)))
            .ToArray();
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, definitions);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        decimal[] prices = [4m, 3m, 2m, 5m, 6m, 2m, 1m, 4m, 6m, 2m];
        Candle[] candles = prices.Select((price, index) => ladder
                ? new Candle(DateTimeOffset.UnixEpoch.AddHours(index), Open: 100m, High: 110m, Low: 85m, Close: 105m, Volume: 1m)
                : new Candle(DateTimeOffset.UnixEpoch.AddHours(index), price, price, price, price, Volume: 1m))
            .ToArray();
        var input = new BacktestInput(
            compilation.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            candles,
            holdout ? OutOfSampleSplit.Create(fraction: 0.4m) : OutOfSampleSplit.None);
        var full = new MetricsAccumulator(input.Settings.StartingCapital, input.SampleRanges);
        var executions = 0;
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnLedger(Arg.Any<BacktestLedgerEntry>()))
            .Do(call =>
            {
                if (call.Arg<BacktestLedgerEntry>()!.Kind == BacktestLedgerKind.Filled)
                {
                    executions++;
                }
            });
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>()))
            .Do(call =>
            {
                full = full.Advance(call.Arg<CandleSnapshot>()!, executions);
                executions = 0;
            });
        var engine = new BacktestEngine();
        BacktestResult baseline = engine.Run(input, observer);
        var metricsOnly = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        BacktestResult narrow = engine.Run(input, metricsOnly);

        baseline.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        baseline.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Filled).ShouldBeGreaterThan(0);
        narrow.Terminal.Outcome.ShouldBe(baseline.Terminal.Outcome);
        narrow.Terminal.Equity.ShouldBe(baseline.Terminal.Equity);
        narrow.Terminal.TotalPnl.ShouldBe(baseline.Terminal.TotalPnl);
        AssertSegment(metricsOnly.Snapshot.Full, full.Snapshot.Full);
        AssertSegment(metricsOnly.Snapshot.InSample, full.Snapshot.InSample);

        if (holdout)
        {
            AssertSegment(metricsOnly.Snapshot.OutOfSample.ShouldNotBeNull(), full.Snapshot.OutOfSample.ShouldNotBeNull());
        }
        else
        {
            metricsOnly.Snapshot.OutOfSample.ShouldBeNull();
        }

        metricsOnly.Snapshot.Diagnostics.ShouldBe(full.Snapshot.Diagnostics);
    }

    private static void AssertSegment(SampleMetrics actual, SampleMetrics expected)
    {
        actual.StartIndex.ShouldBe(expected.StartIndex);
        actual.EndExclusive.ShouldBe(expected.EndExclusive);
        actual.ObservedCandles.ShouldBe(expected.ObservedCandles);
        actual.BoundaryEquity.ShouldBe(expected.BoundaryEquity);
        actual.EndEquity.ShouldBe(expected.EndEquity);
        actual.TotalPnl.ShouldBe(expected.TotalPnl);
        actual.RealizedPnl.ShouldBe(expected.RealizedPnl);
        actual.UnrealizedPnl.ShouldBe(expected.UnrealizedPnl);
        actual.ReturnPercent.ShouldBe(expected.ReturnPercent);
        actual.FeesBase.ShouldBe(expected.FeesBase);
        actual.FeesQuote.ShouldBe(expected.FeesQuote);
        actual.MaxDrawdownPercent.ShouldBe(expected.MaxDrawdownPercent);
        actual.Executions.ShouldBe(expected.Executions);
    }

    [Test]
    public void ScalarMetricsRejectMismatchedStartingCapital()
    {
        var accumulator = new MetricsAccumulator(startingCapital: 500m, new SampleRanges(InSampleBars: 1, OutOfSampleBars: 0));
        var observation = new CandleMetricObservation(
            Sequence: 1,
            CandleIndex: 0,
            StartingCapital: 1000m,
            Equity: 1000m,
            UnrealizedPnl: 0m,
            RealizedPnl: 0m,
            FeesBase: 0m,
            FeesQuote: 0m);

        Should.Throw<ArgumentException>(() => accumulator.Advance(observation, newExecutions: 0));
    }
}