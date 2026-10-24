using NSubstitute;

using ProfitGraph.Application.Optimization;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Execution;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Application.Optimization;

public sealed class OptimizationTrialRunnerTests
{
    [Test]
    public async Task AllInvalidLargeSpaceStopsAtCandidateLimitAndDeliversRejectedPrefix()
    {
        OptimizationRunInputInternal source = OptimizationRunnerFixture.Sma();
        var space = new OptimizationSearchSpace(
        [
            new OptimizationParameterRange(new PortReference("fastPeriod", "value"), min: 100m, max: 1000000000m, step: 1m, ValueType.Integer),
            new OptimizationParameterRange(new PortReference("slowPeriod", "value"), min: 2m, max: 3m, step: 1m, ValueType.Integer)
        ]);
        OptimizationRunInputInternal input = source with { SearchSpace = space, Budget = 2 };
        var received = new List<OptimizationTrialResult>();
        var failure = await Should.ThrowAsync<OptimizationException>(() => Runner(workers: 8)
            .RunAsync(
                input,
                trial =>
                {
                    received.Add(trial);

                    return ValueTask.CompletedTask;
                })
            .WaitAsync(TimeSpan.FromSeconds(10)));

        failure.Code.ShouldBe("candidate_limit_exceeded");
        received.Count.ShouldBe(space.CandidateLimit(input.Budget));
        received.ShouldAllBe(trial => trial.Status == OptimizationTrialStatus.RejectedByTemplate);
        received.Select(trial => trial.Trial.Index).ShouldBe(Enumerable.Range(start: 0, received.Count));
    }

    [Test]
    public async Task AlreadyCancelledRunDeliversNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var received = 0;
        OptimizationRunResultInternal result = await Runner(workers: 8)
            .RunAsync(
                OptimizationRunnerFixture.Sma(),
                _ =>
                {
                    received++;

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);
        result.IsCancelled.ShouldBeTrue();
        result.CompletedTrials.ShouldBe(0);
        received.ShouldBe(0);
    }

    [Test]
    public async Task CancellationDuringPrefilterDeliversAcceptedRejectionsWithoutCandidateLimitFailure()
    {
        using var cancellation = new CancellationTokenSource();
        OptimizationRunInputInternal source = OptimizationRunnerFixture.Sma();
        var space = new OptimizationSearchSpace(
        [
            new OptimizationParameterRange(new PortReference("fastPeriod", "value"), min: 100m, max: 1000000000m, step: 1m, ValueType.Integer),
            new OptimizationParameterRange(new PortReference("slowPeriod", "value"), min: 2m, max: 3m, step: 1m, ValueType.Integer)
        ]);
        var received = new List<OptimizationTrialResult>();
        OptimizationRunResultInternal result = await Runner(workers: 8)
            .RunAsync(
                source with { SearchSpace = space, Budget = 2 },
                trial =>
                {
                    received.Add(trial);
                    cancellation.Cancel();

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);

        result.IsCancelled.ShouldBeTrue();
        result.CompletedTrials.ShouldBe(0);
        received.Count.ShouldBeGreaterThan(0);
        received.Count.ShouldBeLessThan(space.CandidateLimit(budget: 2));
        result.RejectedByTemplate.ShouldBe(received.Count);
        received.Select(trial => trial.Trial.Index).ShouldBe(Enumerable.Range(start: 0, received.Count));
    }

    [Test]
    public async Task CancelledRunDrainsEveryCompletedResultAndReportsDeliveredProgress()
    {
        using var cancellation = new CancellationTokenSource();
        OptimizationRunInputInternal template = OptimizationRunnerFixture.Sma(maxFast: 20, maxSlow: 30);
        OptimizationRunInputInternal input = template with { Template = null };
        var received = new List<OptimizationTrialResult>();
        var progress = new List<OptimizationProgressInternal>();
        var reporter = Substitute.For<IProgress<OptimizationProgressInternal>>();
        reporter.When(value => value.Report(Arg.Any<OptimizationProgressInternal>()))
            .Do(call => progress.Add(call.Arg<OptimizationProgressInternal>().ShouldNotBeNull()));
        OptimizationRunResultInternal result = await Runner(workers: 1)
            .RunAsync(
                input,
                trial =>
                {
                    received.Add(trial);

                    if (received.Count == 3)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                reporter,
                cancellation.Token);

        result.IsCancelled.ShouldBeTrue();
        result.CompletedTrials.ShouldBe(received.Count);
        result.RejectedByTemplate.ShouldBe(0);
        received.Count.ShouldBeGreaterThanOrEqualTo(3);
        received.Count.ShouldBeLessThan(result.TotalTrials);
        received.Select(trial => trial.Trial.Index).Order().ShouldBe(Enumerable.Range(start: 0, received.Count));
        progress.Select(value => value.Completed).ShouldBe(Enumerable.Range(start: 0, received.Count + 1));
        progress.ShouldAllBe(value => value.Total == result.TotalTrials);
        result.Ranking.Select(trial => trial.Trial.Index).Order().ShouldBe(received.Select(trial => trial.Trial.Index).Order());
    }

    [Test]
    public async Task FailedTrialPreservesEngineDiagnosticAndOtherTrialsComplete()
    {
        var graph = new StrategyGraph(
            [
                EngineGraphFixture.Node("numerator", "Constant", new Dictionary<string, ParamValue> { ["value"] = EngineGraphFixture.Number(1m) }),
                EngineGraphFixture.Node(
                    "denominator",
                    "Constant",
                    new Dictionary<string, ParamValue>
                    {
                        ["value"] = new DecimalOptimizeParamValue(Min: 0m, Max: 1m, Step: 1m)
                    }),
                EngineGraphFixture.Node("divide", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = EngineGraphFixture.Text("Divide") })
            ],
            [EngineGraphFixture.Wire("numerator", "divide", "a"), EngineGraphFixture.Wire("denominator", "divide", "b")]);
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Create(graph);
        OptimizationRunResultInternal result = await Runner(workers: 8).RunAsync(input, _ => ValueTask.CompletedTask);

        result.CompletedTrials.ShouldBe(2);
        result.Ranking.Count(trial => trial.Status == OptimizationTrialStatus.Completed).ShouldBe(1);
        OptimizationTrialResult failed = result.Ranking.Single(trial => trial.Status == OptimizationTrialStatus.Failed);
        var engineInput = new BacktestInput(input.CompiledGraph, failed.Trial.Selections, input.Settings, input.Rules, input.History.Slice(input.InSampleBars));
        BacktestResult direct = new BacktestEngine().Run(engineInput);
        direct.Terminal.Outcome.ShouldBe(BacktestOutcome.Failed);
        failed.FailureCode.ShouldBe(direct.Terminal.Diagnostic.ShouldNotBeNull().Code);
        failed.ObjectiveValue.ShouldBeNull();
    }

    [Test]
    public async Task OneAndEightWorkersHaveExactlyTheSameRankingAndPrefilterCounts()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma();
        var received = new List<OptimizationTrialResult>();
        OptimizationRunResultInternal sequential = await Runner(workers: 1)
            .RunAsync(
                input,
                result =>
                {
                    received.Add(result);

                    return ValueTask.CompletedTask;
                });
        OptimizationRunResultInternal parallel = await Runner(workers: 8).RunAsync(input, _ => ValueTask.CompletedTask);

        sequential.CompletedTrials.ShouldBe(6);
        sequential.TotalTrials.ShouldBe(6);
        sequential.RejectedByTemplate.ShouldBe(3);
        sequential.IsCancelled.ShouldBeFalse();
        received.Count.ShouldBe(9);
        sequential.Ranking.Count(result => result.Status == OptimizationTrialStatus.RejectedByTemplate).ShouldBe(3);
        parallel.Ranking.Select(ResultKey).ShouldBe(sequential.Ranking.Select(ResultKey));
    }

    [Test]
    public async Task ReceiverFailureReleasesBlockedWorkers()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 20, maxSlow: 30);
        Task<OptimizationRunResultInternal> run = Runner(workers: 8).RunAsync(input, _ => throw new InvalidOperationException("receiver failed"));
        var error = await Should.ThrowAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        error.Message.ShouldBe("receiver failed");
    }

    [Test]
    public async Task TemplateRejectionsDoNotConsumeTheExecutionBudget()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma() with { Budget = 4 };
        OptimizationRunResultInternal result = await Runner(workers: 2).RunAsync(input, _ => ValueTask.CompletedTask);

        result.CompletedTrials.ShouldBe(4);
        result.TotalTrials.ShouldBe(4);
        result.RejectedByTemplate.ShouldBeGreaterThan(0);
        result.Ranking.Count.ShouldBe(result.CompletedTrials + result.RejectedByTemplate);
        result.Ranking.Count.ShouldBeGreaterThan(input.Budget);
    }

    [Test]
    public async Task TrialsReplayOnlyInSampleAndMatchDirectMetricsOnlyReplay()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma();
        OptimizationRunResultInternal result = await Runner(workers: 2).RunAsync(input, _ => ValueTask.CompletedTask);

        foreach (OptimizationTrialResult trial in result.Ranking.Where(trial => trial.Status == OptimizationTrialStatus.Completed))
        {
            var engineInput = new BacktestInput(input.CompiledGraph, trial.Trial.Selections, input.Settings, input.Rules, input.History.Slice(input.InSampleBars));
            var observer = new MetricsObserver(input.Settings.StartingCapital, engineInput.SampleRanges);
            BacktestResult expected = new BacktestEngine().Run(engineInput, observer, options: new BacktestRunOptions { RetainLedger = false });
            expected.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
            expected.Ledger.ShouldBeEmpty();
            trial.Metrics.ShouldBe(observer.Snapshot.InSample);
            trial.Metrics.ShouldNotBeNull().ObservedCandles.ShouldBe(input.InSampleBars);
        }
    }

    [Test]
    public async Task UnexpectedExceptionInOneTrialBecomesFailureAndOtherTrialsFinish()
    {
        var graph = new StrategyGraph(
            [EngineGraphFixture.Node("choice", "Constant", new Dictionary<string, ParamValue> { ["value"] = new DecimalOptimizeParamValue(Min: 1m, Max: 2m, Step: 1m) })],
            Array.Empty<GraphConnection>());
        OptimizationRunInputInternal source = OptimizationRunnerFixture.Create(graph);
        var broaderSpace = new OptimizationSearchSpace(
            [new OptimizationParameterRange(new PortReference("choice", "value"), min: 1m, max: 3m, step: 1m, ValueType.Scalar)]);
        OptimizationRunInputInternal input = source with { SearchSpace = broaderSpace };
        OptimizationRunResultInternal result = await Runner(workers: 2).RunAsync(input, _ => ValueTask.CompletedTask);

        result.CompletedTrials.ShouldBe(3);
        result.Ranking.Count(trial => trial.Status == OptimizationTrialStatus.Completed).ShouldBe(2);
        OptimizationTrialResult failed = result.Ranking.Single(trial => trial.Status == OptimizationTrialStatus.Failed);
        failed.Trial.Selections.OptimizedValues.Single().Value.ShouldBe(3m);
        failed.FailureCode.ShouldBe("trial_exception");
        failed.ElapsedMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [TestCase(OptimizationSearchMethod.Grid)]
    [TestCase(OptimizationSearchMethod.Random)]
    public async Task RandomAndGridSearchRankTheSameWithOneAndEightWorkers(OptimizationSearchMethod method)
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 20, maxSlow: 30) with { Budget = 40, Seed = 42, Method = method };
        OptimizationRunResultInternal sequential = await Runner(workers: 1).RunAsync(input, _ => ValueTask.CompletedTask);
        OptimizationRunResultInternal parallel = await Runner(workers: 8).RunAsync(input, _ => ValueTask.CompletedTask);

        sequential.CompletedTrials.ShouldBe(40);
        parallel.Ranking.Select(ResultKey).ShouldBe(sequential.Ranking.Select(ResultKey));
    }

    private static OptimizationTrialRunner Runner(int workers)
    {
        return new OptimizationTrialRunner(new OptimizationTrialRunnerOptions { MaxWorkers = workers, ChannelCapacity = 1 }, TimeProvider.System);
    }

    private static (int Index, OptimizationTrialStatus Status, decimal? Objective, SampleMetrics? Metrics, string? Failure) ResultKey(OptimizationTrialResult result)
    {
        return (result.Trial.Index, result.Status, result.ObjectiveValue, result.Metrics, result.FailureCode);
    }
}