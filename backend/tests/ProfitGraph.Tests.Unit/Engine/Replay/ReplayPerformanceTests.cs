using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     Explicit offline throughput evidence, including warmup and retained-ledger costs; no product throughput SLO is asserted.
/// </summary>
public sealed class ReplayPerformanceTests
{
    private static readonly BacktestRunOptions TrialOptions = new BacktestRunOptions { RetainLedger = false };

    [TestCase("Sma", arg2: 10000)]
    [TestCase("Ladder", arg2: 500)]
    [TestCase("Ladder", arg2: 2000)]
    [TestCase("Ladder", arg2: 10000)]
    [TestCase("BlockSma", arg2: 10000)]
    [TestCase("BlockLadder", arg2: 500)]
    [TestCase("BlockLadder", arg2: 2000)]
    [TestCase("BlockLadder", arg2: 10000)]
    [Explicit]
    public void FixedOfflineReplayReportsMedianIncludingWarmup(string workload, int count)
    {
        StrategyGraph graph = CreateGraph(workload);
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
        var input = new BacktestInput(
            compiled,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            Timeframe.Parse("1h"),
            Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)),
            Enumerable.Range(start: 10, count)
                .Select(index => workload.EndsWith("Sma", StringComparison.Ordinal) ? Bar(index) : Bar(index, low: 85m, high: 110m, close: 105m)));
        var engine = new BacktestEngine();
        engine.Run(input).Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        var rates = new List<double>();
        var allocations = new List<long>();
        var elapsedSamples = new List<double>();
        var ledgerCount = 0;
        var fillCount = 0;

        for (var sample = 0; sample < 3; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            BacktestResult result = engine.Run(input);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            rates.Add(count / elapsed);
            elapsedSamples.Add(elapsed);
            result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
            ledgerCount = result.Ledger.Count;
            fillCount = result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Filled);
        }

        if (workload.EndsWith("Sma", StringComparison.Ordinal))
        {
            fillCount.ShouldBe(0);
        }
        else
        {
            fillCount.ShouldBeGreaterThan(0);
        }

        for (var sample = 0; sample < elapsedSamples.Count; sample++)
        {
            TestContext.Progress.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"workload={workload}; sample={sample + 1}; elapsed_seconds={elapsedSamples[sample]:R}; candles_sec={rates[sample]:R}; allocated_bytes={allocations[sample]}"));
        }

        TestContext.Progress.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"workload={workload}; bars={count}; warmup=10 included; measurements=3; median_candles_sec={rates.Order().ElementAt(1):F2}; " +
                $"median_allocated_bytes={allocations.Order().ElementAt(1)}; ledger={ledgerCount}; fills={fillCount}; observer=null; policy={BacktestResult.ExecutionPolicyVersion}; " +
                $"runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; architecture={RuntimeInformation.ProcessArchitecture}; logical_cpus={Environment.ProcessorCount}"));
    }

    [TestCase("Sma", arg2: 10000)]
    [TestCase("Ladder", arg2: 2000)]
    [TestCase("Ladder", arg2: 10000)]
    [Explicit]
    public void MetricsOnlyTrialReportsMedian(string workload, int count)
    {
        (CompiledTradingGraph graph, PreparedReplay prepared, ExecutionSettings settings, InstrumentRules rules) = PrepareTrial(workload, count);
        var engine = new BacktestEngine();
        RunMetricsTrial(engine, graph, prepared, settings, rules).Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        var rates = new List<double>();
        var allocations = new List<long>();

        for (var sample = 0; sample < 3; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            (BacktestResult Result, BacktestMetricsSnapshot Metrics) trial = RunMetricsTrial(engine, graph, prepared, settings, rules);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            trial.Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
            trial.Result.Ledger.ShouldBeEmpty();
            trial.Metrics.Full.ObservedCandles.ShouldBe(count);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            rates.Add(count / elapsed);
            TestContext.Progress.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mode=metrics-only; workload={workload}; sample={sample + 1}; elapsed_seconds={elapsed:R}; candles_sec={rates[^1]:R}; allocated_bytes={allocations[^1]}; " +
                    $"ledger_facts={trial.Result.LedgerCount}; fills={trial.Result.FillCount}; orders={trial.Result.OrderCount}"));
        }

        TestContext.Progress.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"mode=metrics-only; workload={workload}; bars={count}; warmup=10 included; measurements=3; " +
                $"median_candles_sec={rates.Order().ElementAt(1):F2}; median_allocated_bytes={allocations.Order().ElementAt(1)}; " +
                $"runtime={RuntimeInformation.FrameworkDescription}; logical_cpus={Environment.ProcessorCount}"));
    }

    [TestCase("Sma", arg2: 8760, arg3: 24)]
    [TestCase("Ladder", arg2: 2000, arg3: 24)]
    [Explicit]
    public void SharedPreparedReplayScalesWithIdenticalTrialResults(string workload, int count, int trials)
    {
        (CompiledTradingGraph graph, PreparedReplay prepared, ExecutionSettings settings, InstrumentRules rules) = PrepareTrial(workload, count);
        var engine = new BacktestEngine();
        (BacktestResult Result, BacktestMetricsSnapshot Metrics) expected = RunMetricsTrial(engine, graph, prepared, settings, rules);
        expected.Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        int[] workers = [1, 2, 4, 8, Environment.ProcessorCount];

        foreach (int workerCount in workers.Distinct())
        {
            long start = Stopwatch.GetTimestamp();
            Parallel.For(
                fromInclusive: 0,
                trials,
                new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                _ =>
                {
                    (BacktestResult Result, BacktestMetricsSnapshot Metrics) actual = RunMetricsTrial(engine, graph, prepared, settings, rules);
                    actual.Result.Terminal.Outcome.ShouldBe(expected.Result.Terminal.Outcome);
                    actual.Result.Terminal.ProcessedCandles.ShouldBe(expected.Result.Terminal.ProcessedCandles);
                    actual.Result.Terminal.Equity.ShouldBe(expected.Result.Terminal.Equity);
                    actual.Result.Terminal.TotalPnl.ShouldBe(expected.Result.Terminal.TotalPnl);
                    actual.Result.LedgerCount.ShouldBe(expected.Result.LedgerCount);
                    actual.Result.FillCount.ShouldBe(expected.Result.FillCount);
                    actual.Result.OrderCount.ShouldBe(expected.Result.OrderCount);
                    actual.Result.Diagnostics.ShouldBe(expected.Result.Diagnostics);
                    actual.Metrics.Full.ShouldBe(expected.Metrics.Full);
                    actual.Metrics.InSample.ShouldBe(expected.Metrics.InSample);
                    actual.Metrics.OutOfSample.ShouldBe(expected.Metrics.OutOfSample);
                    actual.Metrics.Diagnostics.ShouldBe(expected.Metrics.Diagnostics);
                });
            double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            double throughput = (double)trials * count / elapsed;
            double estimatedThousandYearlySeconds = 1000d * 8760d / throughput;
            TestContext.Progress.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mode=parallel-metrics-only; workload={workload}; bars_per_trial={count}; trials={trials}; workers={workerCount}; " +
                    $"elapsed_seconds={elapsed:R}; total_candles_sec={throughput:R}; estimated_1000x8760_seconds={estimatedThousandYearlySeconds:R}; " +
                    $"identical_results={trials}"));
        }
    }

    [TestCase("Sma")]
    [TestCase("Ladder")]
    [TestCase("BlockSma")]
    [TestCase("BlockLadder")]
    [Explicit]
    public void YearlyFiveMinuteMetricsOnlyTrialReportsMedian(string workload)
    {
        const int count = 84000;
        const int seed = 20261012;
        CompiledTradingGraph graph = new TradingGraphCompiler().Compile(CreateGraph(workload), Catalog).Graph.ShouldNotBeNull();
        var random = new Random(seed);
        var candles = new Candle[count + 10];
        var price = 0.1m;

        for (var index = 0; index < candles.Length; index++)
        {
            decimal open = price;
            price = Math.Clamp(open + random.Next(minValue: -10, maxValue: 11) / 1000m, min: 0.05m, max: 0.15m);
            candles[index] = new Candle(
                DateTimeOffset.UnixEpoch.AddMinutes(5 * index),
                open,
                Math.Max(open, price) + 0.018m,
                Math.Min(open, price) - 0.018m,
                price,
                Volume: 1m);
        }

        var prepared = new PreparedReplay(Timeframe.Parse("5m"), candles.Take(10), candles.Skip(10));
        var settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var rules = new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6);
        var engine = new BacktestEngine();
        RunMetricsTrial(engine, graph, prepared, settings, rules).Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        var elapsedSamples = new List<double>();
        var allocations = new List<long>();

        for (var sample = 0; sample < 3; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            (BacktestResult Result, BacktestMetricsSnapshot Metrics) trial = RunMetricsTrial(engine, graph, prepared, settings, rules);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            elapsedSamples.Add(elapsed);
            trial.Result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
            trial.Result.Ledger.ShouldBeEmpty();
            trial.Metrics.Full.ObservedCandles.ShouldBe(count);
            trial.Result.FillCount.ShouldBeGreaterThan(0);
            TestContext.Progress.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mode=yearly-metrics-only; workload={workload}; sample={sample + 1}; elapsed_seconds={elapsed:R}; " +
                    $"allocated_bytes={allocations[^1]}; ledger_facts={trial.Result.LedgerCount}; fills={trial.Result.FillCount}; " +
                    $"orders={trial.Result.OrderCount}; total_pnl={trial.Result.Terminal.TotalPnl}; metrics_pnl={trial.Metrics.Full.TotalPnl}"));
        }

        TestContext.Progress.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"mode=yearly-metrics-only; workload={workload}; bars={count}; warmup=10 included; seed={seed}; measurements=3; " +
                $"median_seconds={elapsedSamples.Order().ElementAt(1):R}; median_candles_sec={count / elapsedSamples.Order().ElementAt(1):R}; " +
                $"median_allocated_bytes={allocations.Order().ElementAt(1)}; nodes={graph.Nodes.Count}; " +
                $"runtime={RuntimeInformation.FrameworkDescription}; logical_cpus={Environment.ProcessorCount}"));
    }

    private static StrategyGraph CreateGraph(string workload)
    {
        return workload switch
        {
            "Sma" => SmaStrategyGraphGenerator.Generate(),
            "Ladder" => LadderStrategyGraphGenerator.Generate(),
            "BlockSma" => new StrategyDefinitionLowerer(Catalog).Lower(SmaStrategyBlockFactory.Create()).Graph,
            "BlockLadder" => new StrategyDefinitionLowerer(Catalog).Lower(LadderStrategyBlockFactory.Create()).Graph,
            _ => throw new ArgumentOutOfRangeException(nameof(workload))
        };
    }

    private static (CompiledTradingGraph Graph, PreparedReplay Prepared, ExecutionSettings Settings, InstrumentRules Rules) PrepareTrial(string workload, int count)
    {
        StrategyGraph graph = CreateGraph(workload);
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
        var settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var rules = new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6);
        var prepared = new PreparedReplay(
            Timeframe.Parse("1h"),
            Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)),
            Enumerable.Range(start: 10, count)
                .Select(index => workload.EndsWith("Sma", StringComparison.Ordinal) ? Bar(index) : Bar(index, low: 85m, high: 110m, close: 105m)));

        return (compiled, prepared, settings, rules);
    }

    private static (BacktestResult Result, BacktestMetricsSnapshot Metrics) RunMetricsTrial(
        BacktestEngine engine,
        CompiledTradingGraph graph,
        PreparedReplay prepared,
        ExecutionSettings settings,
        InstrumentRules rules)
    {
        var input = new BacktestInput(graph, EngineExecutionInputs.Empty, settings, rules, prepared);
        var observer = new MetricsObserver(settings.StartingCapital, input.SampleRanges);
        BacktestResult result = engine.Run(input, observer, options: TrialOptions);

        return (result, observer.Snapshot);
    }
}