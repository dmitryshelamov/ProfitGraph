using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Execution;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Application.Optimization;

/// <summary>
///     Explicit offline runner scaling evidence against the Profit Fox Designer targets (1000 year-hourly trials under 30
///     seconds for SMA and under 10 minutes for Ladder); no product throughput assertion.
/// </summary>
public sealed class OptimizationPerformanceTests
{
    [TestCase("Sma", arg2: 8760)]
    [TestCase("Ladder", arg2: 2000)]
    [Explicit]
    public async Task TrialRunnerReportsScaling(string workload, int count)
    {
        StrategyGraph template = workload == "Sma" ? SmaStrategyGraphGenerator.Generate() : LadderStrategyGraphGenerator.Generate();
        var range = new GraphNode(
            "optimizerTrial",
            "Parameter",
            Catalog.Single(definition => definition.TypeKey == "Parameter").SchemaVersion,
            new Dictionary<string, ParamValue>
            {
                ["name"] = new FixedParamValue(new TextParamLiteral("optimizerTrial")),
                ["value"] = new DecimalOptimizeParamValue(Min: 1m, Max: 24m, Step: 1m)
            },
            ValueType.Integer);
        var graph = new StrategyGraph(template.Nodes.Append(range).ToArray(), template.Connections, template.TemplateKind);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph compiled = compilation.Graph.ShouldNotBeNull();
        var prepared = new PreparedReplay(
            Timeframe.Parse("1h"),
            Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)),
            Enumerable.Range(start: 10, count).Select(index => workload == "Sma" ? Bar(index) : Bar(index, low: 85m, high: 110m, close: 105m)));
        var input = new OptimizationRunInputInternal(
            graph.TemplateKind,
            compiled,
            prepared,
            count,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            OptimizationSearchSpace.FromGraph(compiled),
            Constraints: new OptimizationConstraints(minExecutions: 0));
        OptimizationRunResultInternal expected = await CreateRunner(workers: 1).RunAsync(input, _ => ValueTask.CompletedTask);
        expected.CompletedTrials.ShouldBe(24);
        expected.Ranking.ShouldAllBe(result => result.Status == OptimizationTrialStatus.Completed);
        expected.Ranking.Select(result => result.Metrics).Distinct().Count().ShouldBe(1);

        foreach (int workers in new[] { 1, 2, 4, 8, 12 })
        {
            var elapsedSamples = new List<double>();

            for (var sample = 0; sample < 3; sample++)
            {
                OptimizationTrialRunner runner = CreateRunner(workers);
                long allocated = GC.GetTotalAllocatedBytes(precise: true);
                int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
                long start = Stopwatch.GetTimestamp();
                OptimizationRunResultInternal actual = await runner.RunAsync(input, _ => ValueTask.CompletedTask);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
                elapsedSamples.Add(elapsed);
                long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
                actual.CompletedTrials.ShouldBe(expected.CompletedTrials);
                actual.Ranking.Select(result => (result.Trial.Index, result.Status, result.ObjectiveValue, result.Metrics, result.FailureCode))
                    .ShouldBe(expected.Ranking.Select(result => (result.Trial.Index, result.Status, result.ObjectiveValue, result.Metrics, result.FailureCode)));
                TestContext.Progress.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"mode=optimization-runner; workload={workload}; bars_per_trial={count}; trials=24; workers={workers}; sample={sample + 1}; " +
                        $"elapsed_seconds={elapsed:R}; total_candles_sec={24d * count / elapsed:R}; allocated_bytes={allocatedBytes}; " +
                        $"gc0={GC.CollectionCount(0) - collections[0]}; gc1={GC.CollectionCount(1) - collections[1]}; gc2={GC.CollectionCount(2) - collections[2]}; server_gc={GCSettings.IsServerGC}"));
            }

            double median = elapsedSamples.Order().ElementAt(1);
            TestContext.Progress.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mode=optimization-runner-median; workload={workload}; workers={workers}; elapsed_seconds={median:R}; total_candles_sec={24d * count / median:R}; " +
                    $"estimated_1000x8760_seconds={1000d * 8760d / (24d * count / median):R}; server_gc={GCSettings.IsServerGC}; " +
                    $"runtime={RuntimeInformation.FrameworkDescription}; logical_cpus={Environment.ProcessorCount}"));
        }
    }

    private static OptimizationTrialRunner CreateRunner(int workers)
    {
        return new OptimizationTrialRunner(new OptimizationTrialRunnerOptions { MaxWorkers = workers }, TimeProvider.System);
    }
}