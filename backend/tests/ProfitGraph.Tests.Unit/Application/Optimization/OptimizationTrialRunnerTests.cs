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

    [TestCase(OptimizationSearchMethod.Grid, arg2: 1)]
    [TestCase(OptimizationSearchMethod.Grid, arg2: 8)]
    [TestCase(OptimizationSearchMethod.Random, arg2: 1)]
    [TestCase(OptimizationSearchMethod.Random, arg2: 8)]
    public async Task ResumedSearchRestoresStoredTrialsAndRanksExactlyAsAnUninterruptedOne(OptimizationSearchMethod method, int workers)
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 8, maxSlow: 12) with { Budget = 30, Seed = 7, Method = method };
        var delivered = new List<OptimizationTrialResult>();
        OptimizationRunResultInternal uninterrupted = await Runner(workers)
            .RunAsync(
                input,
                trial =>
                {
                    delivered.Add(trial);

                    return ValueTask.CompletedTask;
                });
        OptimizationTrialInternal[] stored = [.. delivered.Where(trial => trial.Trial.Index % 3 != 1).Select((trial, index) => Persisted(index + 1, trial))];
        var resumedDelivery = new List<OptimizationTrialResult>();
        OptimizationRunResultInternal resumed = await Runner(workers)
            .RunAsync(
                input,
                trial =>
                {
                    resumedDelivery.Add(trial);

                    return ValueTask.CompletedTask;
                },
                persistedTrials: stored);

        // A restored trial keeps only the stored metrics, so the ranking is compared by what ranks it.
        resumed.Ranking.Select(RankKey).ShouldBe(uninterrupted.Ranking.Select(RankKey));
        resumed.CompletedTrials.ShouldBe(uninterrupted.CompletedTrials);
        resumed.RejectedByTemplate.ShouldBe(uninterrupted.RejectedByTemplate);
        resumedDelivery.Select(trial => trial.Trial.Index)
            .Order()
            .ShouldBe(delivered.Where(trial => trial.Trial.Index % 3 == 1).Select(trial => trial.Trial.Index).Order());
    }

    [Test]
    public async Task StoredTrialThatDoesNotMatchItsCandidateFailsTheSearch()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma();
        var delivered = new List<OptimizationTrialResult>();
        await Runner(workers: 2)
            .RunAsync(
                input,
                trial =>
                {
                    delivered.Add(trial);

                    return ValueTask.CompletedTask;
                });
        OptimizationTrialInternal stored = Persisted(sequence: 1, delivered.First(trial => trial.Status == OptimizationTrialStatus.Completed));
        OptimizationTrialInternal changed = stored with { Parameters = stored.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value + 100m) };

        var failure = await Should.ThrowAsync<OptimizationException>(() => Runner(workers: 2).RunAsync(input, _ => ValueTask.CompletedTask, persistedTrials: [changed]));

        failure.Code.ShouldBe(OptimizationErrorCodes.PersistedTrialMismatch);
    }

    private static (int Index, OptimizationTrialStatus Status, decimal? Objective, string? Failure) RankKey(OptimizationTrialResult result)
    {
        return (result.Trial.Index, result.Status, result.ObjectiveValue, result.FailureCode);
    }

    private static OptimizationTrialInternal Persisted(long sequence, OptimizationTrialResult trial)
    {
        return new OptimizationTrialInternal(
            sequence,
            trial.Trial.Index,
            trial.Trial.Selections.OptimizedValues.ToDictionary(pair => pair.Key.NodeId, pair => pair.Value),
            OptimizationNames.TrialStatus(trial.Status),
            trial.FailureCode,
            trial.ObjectiveValue,
            trial.Metrics?.ReturnPercent,
            trial.Metrics?.TotalPnl,
            trial.Metrics?.MaxDrawdownPercent,
            trial.Metrics?.Executions ?? 0,
            trial.ElapsedMs,
            trial.Trial.Stage);
    }

    [TestCase(OptimizationSearchMethod.Grid, arg2: 1)]
    [TestCase(OptimizationSearchMethod.Random, arg2: 1)]
    [TestCase(OptimizationSearchMethod.CoarseToFine, arg2: 1)]
    [TestCase(OptimizationSearchMethod.CoarseToFine, arg2: 2)]
    public async Task InterruptedSearchResumesToIdenticalTrialsRankingAndScores(OptimizationSearchMethod method, int interruptedStage)
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with { Method = method, Budget = 100, Seed = 771 };
        OptimizationRunResultInternal uninterrupted = await Runner(workers: 1).RunAsync(input, _ => ValueTask.CompletedTask);
        var persisted = new List<OptimizationTrialInternal>();
        using var cancellation = new CancellationTokenSource();
        var stageReceived = 0;
        OptimizationRunResultInternal interrupted = await Runner(workers: 1)
            .RunAsync(
                input,
                trial =>
                {
                    persisted.Add(Persisted(trial));

                    if (trial.Trial.Stage == interruptedStage && ++stageReceived == 2)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);
        interrupted.IsCancelled.ShouldBeTrue();
        int[] previouslyStored = persisted.Select(trial => trial.TrialIndex).ToArray();
        var delivered = new List<int>();
        OptimizationRunResultInternal resumed = await Runner(workers: 8)
            .RunAsync(
                input,
                trial =>
                {
                    delivered.Add(trial.Trial.Index);
                    persisted.Add(Persisted(trial));

                    return ValueTask.CompletedTask;
                },
                persistedTrials: persisted.ToArray());

        delivered.Intersect(previouslyStored).ShouldBeEmpty();
        persisted.Select(trial => trial.TrialIndex).Distinct().Count().ShouldBe(persisted.Count);
        persisted.OrderBy(trial => trial.TrialIndex)
            .Select(StoredKey)
            .ShouldBe(uninterrupted.Ranking.Select(Persisted).OrderBy(trial => trial.TrialIndex).Select(StoredKey));
        resumed.Ranking.Select(trial => (trial.Trial.Index, trial.Trial.GridIndex, trial.Trial.Stage, trial.Status, trial.ObjectiveValue))
            .ShouldBe(uninterrupted.Ranking.Select(trial => (trial.Trial.Index, trial.Trial.GridIndex, trial.Trial.Stage, trial.Status, trial.ObjectiveValue)));
        resumed.NeighborhoodScores.OrderBy(pair => pair.Key).ShouldBe(uninterrupted.NeighborhoodScores.OrderBy(pair => pair.Key));
        resumed.CompletedTrials.ShouldBe(uninterrupted.CompletedTrials);
        resumed.TotalTrials.ShouldBe(uninterrupted.TotalTrials);
        resumed.RejectedByTemplate.ShouldBe(uninterrupted.RejectedByTemplate);
    }

    [TestCase(arg1: 1, arg2: false)]
    [TestCase(arg1: 8, arg2: false)]
    [TestCase(arg1: 1, arg2: true)]
    [TestCase(arg1: 8, arg2: true)]
    public async Task TpeResumesAfterStartupOrFirstGuidedBatchWithIdenticalTrials(int workers, bool afterGuidedBatch)
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with
        {
            Method = OptimizationSearchMethod.Tpe,
            Budget = 100,
            Seed = 771
        };
        OptimizationTpePlan.TryCreate(input.SearchSpace, input.Budget, out OptimizationTpePlan? plan).ShouldBeTrue();
        OptimizationRunResultInternal sequential = await Runner(workers: 1).RunAsync(input, _ => ValueTask.CompletedTask);
        OptimizationRunResultInternal parallel = await Runner(workers: 8).RunAsync(input, _ => ValueTask.CompletedTask);
        var persisted = new List<OptimizationTrialInternal>();
        using var cancellation = new CancellationTokenSource();
        int stopAfter = plan!.StartupCount + (afterGuidedBatch ? plan.BatchSize : 0);
        var evaluated = 0;
        OptimizationRunResultInternal interrupted = await Runner(workers)
            .RunAsync(
                input,
                result =>
                {
                    persisted.Add(Persisted(result));

                    if (result.Status != OptimizationTrialStatus.RejectedByTemplate && ++evaluated == stopAfter)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);
        interrupted.IsCancelled.ShouldBeTrue();
        evaluated.ShouldBe(stopAfter);
        interrupted.NeighborhoodScores.ShouldBeEmpty();
        int[] previouslyStored = persisted.Select(trial => trial.TrialIndex).ToArray();
        var newlyDelivered = new List<int>();
        OptimizationRunResultInternal resumed = await Runner(workers == 1 ? 8 : 1)
            .RunAsync(
                input,
                result =>
                {
                    newlyDelivered.Add(result.Trial.Index);
                    persisted.Add(Persisted(result));

                    return ValueTask.CompletedTask;
                },
                persistedTrials: persisted.ToArray());

        newlyDelivered.Intersect(previouslyStored).ShouldBeEmpty();
        persisted.OrderBy(trial => trial.TrialIndex)
            .Select(StoredKey)
            .ShouldBe(sequential.Ranking.Select(Persisted).OrderBy(trial => trial.TrialIndex).Select(StoredKey));
        parallel.Ranking.Select(Persisted)
            .OrderBy(trial => trial.TrialIndex)
            .Select(StoredKey)
            .ShouldBe(persisted.OrderBy(trial => trial.TrialIndex).Select(StoredKey));
        resumed.NeighborhoodScores.OrderBy(pair => pair.Key).ShouldBe(sequential.NeighborhoodScores.OrderBy(pair => pair.Key));
        parallel.NeighborhoodScores.OrderBy(pair => pair.Key).ShouldBe(sequential.NeighborhoodScores.OrderBy(pair => pair.Key));
        resumed.CompletedTrials.ShouldBe(input.Budget);
        resumed.TotalTrials.ShouldBe(input.Budget);
        resumed.Ranking.Where(result => result.Status != OptimizationTrialStatus.RejectedByTemplate)
            .Count(result => result.Trial.Stage == 1)
            .ShouldBe(plan.StartupCount);
        resumed.Ranking.ShouldContain(result => result.Trial.Stage == 2 && result.Status == OptimizationTrialStatus.RejectedByTemplate);
        resumed.Ranking.Select(result => result.Trial.GridIndex).Distinct().Count().ShouldBe(resumed.Ranking.Count);
    }

    [Test]
    public async Task CancellationAfterStageOneDoesNotStartRefinementOrScoreTrials()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with
        {
            Budget = 100,
            Method = OptimizationSearchMethod.CoarseToFine
        };
        OptimizationCoarseToFinePlan.TryCreate(input.SearchSpace, input.Budget, out OptimizationCoarseToFinePlan? plan).ShouldBeTrue();
        using var cancellation = new CancellationTokenSource();
        var received = 0;
        OptimizationRunResultInternal result = await Runner(workers: 8)
            .RunAsync(
                input,
                _ =>
                {
                    if (++received == (int)plan!.CoarseGridSize)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);

        result.IsCancelled.ShouldBeTrue();
        result.Ranking.ShouldAllBe(trial => trial.Trial.Stage == 1);
        result.NeighborhoodScores.ShouldBeEmpty();
    }

    [Test]
    public async Task CoarseToFineRunsBothStagesAndScoresSameTrialsWithOneAndEightWorkers()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with
        {
            Budget = 100,
            Method = OptimizationSearchMethod.CoarseToFine
        };
        OptimizationRunResultInternal sequential = await Runner(workers: 1).RunAsync(input, _ => ValueTask.CompletedTask);
        OptimizationRunResultInternal parallel = await Runner(workers: 8).RunAsync(input, _ => ValueTask.CompletedTask);

        sequential.IsCancelled.ShouldBeFalse();
        sequential.Ranking.ShouldContain(trial => trial.Trial.Stage == 1);
        sequential.Ranking.ShouldContain(trial => trial.Trial.Stage == 2);
        sequential.Ranking.Select(trial => (trial.Trial.Index, trial.Trial.GridIndex, trial.Trial.Stage, trial.Status, trial.ObjectiveValue))
            .ShouldBe(parallel.Ranking.Select(trial => (trial.Trial.Index, trial.Trial.GridIndex, trial.Trial.Stage, trial.Status, trial.ObjectiveValue)));
        sequential.NeighborhoodScores.OrderBy(pair => pair.Key).ShouldBe(parallel.NeighborhoodScores.OrderBy(pair => pair.Key));
        sequential.NeighborhoodScores.Count.ShouldBe(sequential.CompletedTrials);
        sequential.Ranking.Where(trial => trial.Status == OptimizationTrialStatus.RejectedByTemplate)
            .ShouldAllBe(trial => !sequential.NeighborhoodScores.ContainsKey(trial.Trial.Index));
        sequential.Ranking.Where(trial => trial.Trial.Stage == 2).ShouldContain(trial => trial.Status == OptimizationTrialStatus.RejectedByTemplate);
    }

    [Test]
    public async Task PersistedHolesAndEntireStageOneAreRestoredWithoutReexecutingThem()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with
        {
            Method = OptimizationSearchMethod.CoarseToFine,
            Budget = 100
        };
        OptimizationRunResultInternal expected = await Runner(workers: 1).RunAsync(input, _ => ValueTask.CompletedTask);
        OptimizationTrialInternal[] persisted = expected.Ranking.Where(trial => trial.Trial.Stage == 1 || trial.Trial.Index % 2 == 0).Select(Persisted).ToArray();
        var delivered = new List<int>();
        var reported = new List<OptimizationProgressInternal>();
        var progress = Substitute.For<IProgress<OptimizationProgressInternal>>();
        progress.When(reporter => reporter.Report(Arg.Any<OptimizationProgressInternal>()))
            .Do(call => reported.Add(call.Arg<OptimizationProgressInternal>().ShouldNotBeNull()));
        OptimizationRunResultInternal resumed = await Runner(workers: 8)
            .RunAsync(
                input,
                result =>
                {
                    delivered.Add(result.Trial.Index);

                    return ValueTask.CompletedTask;
                },
                progress,
                persistedTrials: persisted)
            .WaitAsync(TimeSpan.FromSeconds(10));

        reported[0].Completed.ShouldBe(persisted.Count(trial => trial.Status != OptimizationNames.TrialStatus(OptimizationTrialStatus.RejectedByTemplate)));
        reported.ShouldAllBe(item => item.Completed <= item.Total);
        reported.Select(item => item.Completed).ShouldBe(reported.Select(item => item.Completed).Order());
        delivered.Order().ShouldBe(expected.Ranking.Select(trial => trial.Trial.Index).Except(persisted.Select(trial => trial.TrialIndex)).Order());
        resumed.Ranking.Select(trial => (trial.Trial.Index, trial.ObjectiveValue)).ShouldBe(expected.Ranking.Select(trial => (trial.Trial.Index, trial.ObjectiveValue)));
        resumed.NeighborhoodScores.OrderBy(pair => pair.Key).ShouldBe(expected.NeighborhoodScores.OrderBy(pair => pair.Key));
    }

    [Test]
    public async Task TpeRejectsMismatchedPersistedGuidedTrial()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with
        {
            Method = OptimizationSearchMethod.Tpe,
            Budget = 100,
            Seed = 771
        };
        OptimizationRunResultInternal expected = await Runner(workers: 1).RunAsync(input, _ => ValueTask.CompletedTask);
        OptimizationTrialResult guided = expected.Ranking.First(result => result.Trial.Stage == 2);
        OptimizationTrialInternal[] stored = expected.Ranking.Select(Persisted)
            .Select(trial => trial.TrialIndex == guided.Trial.Index ? trial with { Stage = 1 } : trial)
            .ToArray();
        var failure = await Should.ThrowAsync<OptimizationException>(() => Runner(workers: 8)
            .RunAsync(input, _ => ValueTask.CompletedTask, persistedTrials: stored));
        failure.Code.ShouldBe("persisted_trial_mismatch");
    }

    private static OptimizationTrialInternal Persisted(OptimizationTrialResult trial)
    {
        return Persisted(sequence: 0, trial);
    }

    private static object StoredKey(OptimizationTrialInternal trial)
    {
        return (trial.TrialIndex, trial.Stage, trial.Status, trial.FailureCode, trial.ReturnPercent, trial.TotalPnl, trial.MaxDrawdownPercent, trial.Objective,
            trial.Executions, string.Join(";", trial.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}")));
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