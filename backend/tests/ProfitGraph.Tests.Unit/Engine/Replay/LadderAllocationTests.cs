using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class LadderAllocationTests
{
    private static readonly BacktestRunOptions TrialOptions = new BacktestRunOptions { RetainLedger = false };

    [Test]
    public void MetricsOnlyLadderKeepsPerCandleAllocationsBelowRegressionLimit()
    {
        const int shortCount = 256;
        const int longCount = 1024;
        BacktestInput shortInput = Prepare(shortCount);
        BacktestInput longInput = Prepare(longCount);
        var engine = new BacktestEngine();
        Run(engine, shortInput).Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Run(engine, longInput).Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        long shortAllocated = MeasureMinimumAllocation(engine, shortInput);
        long longAllocated = MeasureMinimumAllocation(engine, longInput);
        long bytesPerAdditionalCandle = (longAllocated - shortAllocated) / (longCount - shortCount);
        TestContext.Progress.WriteLine(
            $"Ladder metrics-only: short_bytes={shortAllocated}; long_bytes={longAllocated}; bytes_per_additional_candle={bytesPerAdditionalCandle}");

        // Comparing lengths excludes fixed setup costs. The optimized fixture uses about 31.2 KB/candle; 40 KB allows 28% headroom.
        // The same fixture allocated about 66.5 KB/candle before the hot-path changes.
        bytesPerAdditionalCandle.ShouldBeGreaterThan(0);
        bytesPerAdditionalCandle.ShouldBeLessThan(40000);
    }

    [Test]
    public void MetricsOnlyLadderHasExactlyTheSameResultsWithOneAndEightWorkers()
    {
        BacktestInput input = Prepare(count: 64);
        var engine = new BacktestEngine();
        (BacktestResult Result, BacktestMetricsSnapshot Metrics) expected = Run(engine, input);
        string expectedTerminal = JsonSerializer.Serialize(expected.Result.Terminal);
        string expectedMetrics = JsonSerializer.Serialize(expected.Metrics);

        Parallel.For(
            fromInclusive: 0,
            toExclusive: 8,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            _ =>
            {
                (BacktestResult Result, BacktestMetricsSnapshot Metrics) actual = Run(engine, input);
                JsonSerializer.Serialize(actual.Result.Terminal).ShouldBe(expectedTerminal);
                JsonSerializer.Serialize(actual.Metrics).ShouldBe(expectedMetrics);
                actual.Result.LedgerCount.ShouldBe(expected.Result.LedgerCount);
                actual.Result.FillCount.ShouldBe(expected.Result.FillCount);
                actual.Result.OrderCount.ShouldBe(expected.Result.OrderCount);
                actual.Result.Diagnostics.ShouldBe(expected.Result.Diagnostics);
            });
    }

    private static BacktestInput Prepare(int count)
    {
        CompiledTradingGraph graph = new TradingGraphCompiler()
            .Compile(LadderStrategyGraphGenerator.Generate(), Catalog)
            .Graph.ShouldNotBeNull();
        var replay = new PreparedReplay(
            Timeframe.Parse("1h"),
            Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)),
            Enumerable.Range(start: 10, count).Select(index => Bar(index, low: 85m, high: 110m, close: 105m)));

        return new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            replay);
    }

    private static long MeasureMinimumAllocation(BacktestEngine engine, BacktestInput input)
    {
        var minimum = long.MaxValue;

        for (var sample = 0; sample < 3; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            (BacktestResult Result, BacktestMetricsSnapshot Metrics) trial = Run(engine, input);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            trial.Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
            trial.Result.Ledger.ShouldBeEmpty();
            trial.Result.FillCount.ShouldBeGreaterThan(0);
            minimum = Math.Min(minimum, allocated);
        }

        return minimum;
    }

    private static (BacktestResult Result, BacktestMetricsSnapshot Metrics) Run(BacktestEngine engine, BacktestInput input)
    {
        var observer = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        BacktestResult result = engine.Run(input, observer, options: TrialOptions);

        return (result, observer.Snapshot);
    }
}