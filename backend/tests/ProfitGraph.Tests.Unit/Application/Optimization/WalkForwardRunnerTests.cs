using NSubstitute;

using ProfitGraph.Application.Optimization;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Execution;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Optimization.Search;
using ProfitGraph.Domain.Optimization.WalkForward;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Application.Optimization;

public sealed class WalkForwardRunnerTests
{
    [TestCase(WalkForwardScheme.Rolling)]
    [TestCase(WalkForwardScheme.Anchored)]
    public async Task OneAndEightWorkersProduceIdenticalResultsAndCumulativeProgress(WalkForwardScheme scheme)
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma() with { Seed = 771, Method = OptimizationSearchMethod.Random, Budget = 4 };
        var settings = new WalkForwardSettings(windows: 2, trainToTestRatio: 2, scheme);
        var received = new List<OptimizationProgressInternal>();
        var progress = Substitute.For<IProgress<OptimizationProgressInternal>>();
        progress.When(reporter => reporter.Report(Arg.Any<OptimizationProgressInternal>()))
            .Do(call => received.Add(call.Arg<OptimizationProgressInternal>().ShouldNotBeNull()));
        WalkForwardRunResultInternal sequential = await Runner(workers: 1)
            .RunAsync(
                input,
                settings,
                _ => ValueTask.CompletedTask,
                _ => ValueTask.CompletedTask,
                progress);
        WalkForwardRunResultInternal parallel = await Runner(workers: 8)
            .RunAsync(
                input,
                settings,
                _ => ValueTask.CompletedTask,
                _ => ValueTask.CompletedTask);

        sequential.CompletedTrials.ShouldBe(parallel.CompletedTrials);
        sequential.RejectedByTemplate.ShouldBe(parallel.RejectedByTemplate);
        received.Select(value => value.Completed).ShouldBe(Enumerable.Range(start: 0, sequential.CompletedTrials + 1));
        received.ShouldAllBe(value => value.Total == 8);

        for (var index = 0; index < sequential.Windows.Count; index++)
        {
            WalkForwardWindowResult left = sequential.Windows[index];
            WalkForwardWindowResult right = parallel.Windows[index];
            left.Window.ShouldBe(right.Window);
            left.WinnerTrialIndex.ShouldBe(right.WinnerTrialIndex);
            left.Parameters.ShouldNotBeNull().OptimizedValues.ShouldBe(right.Parameters.ShouldNotBeNull().OptimizedValues);
            left.TrainMetrics.ShouldBe(right.TrainMetrics);
            left.TestMetrics.ShouldBe(right.TestMetrics);
            left.Status.ShouldBe(right.Status);
            left.Equity.ShouldBe(right.Equity);
        }

        sequential.Summary.TestTotalPnl.ShouldBe(parallel.Summary.TestTotalPnl);
        sequential.Summary.TestCompoundedReturnPercent.ShouldBe(parallel.Summary.TestCompoundedReturnPercent);
        sequential.Summary.OverallWfe.ShouldBe(parallel.Summary.OverallWfe);
        sequential.Summary.ProfitableWindows.ShouldBe(parallel.Summary.ProfitableWindows);
        sequential.Summary.TestWindows.ShouldBe(parallel.Summary.TestWindows);
        sequential.Summary.Diagnostics.ShouldBe(parallel.Summary.Diagnostics);
        sequential.Summary.ParameterStability.Count.ShouldBe(parallel.Summary.ParameterStability.Count);

        for (var index = 0; index < sequential.Summary.ParameterStability.Count; index++)
        {
            WalkForwardParameterStability left = sequential.Summary.ParameterStability[index];
            WalkForwardParameterStability right = parallel.Summary.ParameterStability[index];
            left.Reference.ShouldBe(right.Reference);
            left.Winners.ShouldBe(right.Winners);
            left.DistinctCount.ShouldBe(right.DistinctCount);
            left.RangeInSteps.ShouldBe(right.RangeInSteps);
        }
    }

    [TestCase(OptimizationSearchMethod.Grid, arg2: false)]
    [TestCase(OptimizationSearchMethod.Random, arg2: false)]
    [TestCase(OptimizationSearchMethod.CoarseToFine, arg2: false)]
    [TestCase(OptimizationSearchMethod.Grid, arg2: true)]
    [TestCase(OptimizationSearchMethod.Tpe, arg2: false)]
    [TestCase(OptimizationSearchMethod.Tpe, arg2: true)]
    public async Task ResumeSkipsFinishedWindowsAndRestoresUnfinishedTrain(OptimizationSearchMethod method, bool allTrainTrialsStored)
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with { Method = method, Budget = 100, Seed = 771 };
        var settings = new WalkForwardSettings(windows: 2, trainToTestRatio: 2);
        var expectedTrials = new List<WalkForwardTrialResultInternal>();
        var expectedScores = new Dictionary<int, IReadOnlyDictionary<int, OptimizationNeighborhoodScore>>();
        WalkForwardRunResultInternal expected = await Runner(workers: 1)
            .RunAsync(
                input,
                settings,
                trial =>
                {
                    expectedTrials.Add(trial);

                    return ValueTask.CompletedTask;
                },
                _ => ValueTask.CompletedTask,
                scoreSink: (window, scores) =>
                {
                    expectedScores.Add(window, scores);

                    return ValueTask.CompletedTask;
                });
        var persisted = new List<OptimizationTrialInternal>();
        var finished = new List<WalkForwardWindowResult>();
        using var cancellation = new CancellationTokenSource();
        var receivedInSecond = 0;
        int interruptAfter = allTrainTrialsStored ? expectedTrials.Count(trial => trial.WindowIndex == 1) : 2;
        WalkForwardRunResultInternal interrupted = await Runner(workers: 1)
            .RunAsync(
                input,
                settings,
                trial =>
                {
                    persisted.Add(Persisted(trial));

                    if (trial.WindowIndex == 1 && ++receivedInSecond == interruptAfter)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                window =>
                {
                    finished.Add(window);

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);
        interrupted.IsCancelled.ShouldBeTrue();
        finished.Count.ShouldBe(1);
        var resumedTrials = new List<WalkForwardTrialResultInternal>();
        var resumedWindows = new List<WalkForwardWindowResult>();
        var resumedScores = new Dictionary<int, IReadOnlyDictionary<int, OptimizationNeighborhoodScore>>();
        WalkForwardRunResultInternal resumed = await Runner(workers: 8)
            .RunAsync(
                input,
                settings,
                trial =>
                {
                    resumedTrials.Add(trial);

                    return ValueTask.CompletedTask;
                },
                window =>
                {
                    resumedWindows.Add(window);

                    return ValueTask.CompletedTask;
                },
                scoreSink: (window, scores) =>
                {
                    resumedScores.Add(window, scores);

                    return ValueTask.CompletedTask;
                },
                persistedTrials: persisted,
                completedWindows: finished);

        resumed.IsCancelled.ShouldBeFalse();
        resumedTrials.ShouldAllBe(trial => trial.WindowIndex == 1);
        resumedTrials.Select(trial => (trial.WindowIndex, trial.Result.Trial.Index))
            .Intersect(persisted.Select(trial => (trial.WindowIndex, trial.TrialIndex)))
            .ShouldBeEmpty();
        resumedWindows.Count.ShouldBe(1);
        resumedWindows[0].Window.Index.ShouldBe(1);
        resumed.CompletedTrials.ShouldBe(expected.CompletedTrials);
        resumed.TotalTrials.ShouldBe(expected.TotalTrials);
        resumed.RejectedByTemplate.ShouldBe(expected.RejectedByTemplate);
        resumedScores[1].OrderBy(pair => pair.Key).ShouldBe(expectedScores[1].OrderBy(pair => pair.Key));

        for (var index = 0; index < expected.Windows.Count; index++)
        {
            WalkForwardWindowResult actual = resumed.Windows[index];
            WalkForwardWindowResult baseline = expected.Windows[index];
            actual.Window.ShouldBe(baseline.Window);
            actual.Status.ShouldBe(baseline.Status);
            actual.WinnerTrialIndex.ShouldBe(baseline.WinnerTrialIndex);
            actual.TrainMetrics.ShouldBe(baseline.TrainMetrics);
            actual.TestMetrics.ShouldBe(baseline.TestMetrics);
            actual.FailureCode.ShouldBe(baseline.FailureCode);
            actual.Equity.ShouldBe(baseline.Equity);
            actual.Parameters.ShouldNotBeNull().OptimizedValues.ShouldBe(baseline.Parameters.ShouldNotBeNull().OptimizedValues);
        }

        resumed.Summary.TestTotalPnl.ShouldBe(expected.Summary.TestTotalPnl);
        resumed.Summary.TestCompoundedReturnPercent.ShouldBe(expected.Summary.TestCompoundedReturnPercent);
        resumed.Summary.OverallWfe.ShouldBe(expected.Summary.OverallWfe);
        resumed.Summary.ProfitableWindows.ShouldBe(expected.Summary.ProfitableWindows);
        resumed.Summary.TestWindows.ShouldBe(expected.Summary.TestWindows);
        resumed.Summary.Diagnostics.ShouldBe(expected.Summary.Diagnostics);
    }

    private static OptimizationTrialInternal Persisted(WalkForwardTrialResultInternal trial)
    {
        OptimizationTrialResult result = trial.Result;

        return new OptimizationTrialInternal(
            Sequence: 0,
            result.Trial.Index,
            result.Trial.Selections.OptimizedValues.ToDictionary(pair => pair.Key.NodeId, pair => pair.Value),
            OptimizationNames.TrialStatus(result.Status),
            result.FailureCode,
            result.ObjectiveValue,
            result.Metrics?.ReturnPercent,
            result.Metrics?.TotalPnl,
            result.Metrics?.MaxDrawdownPercent,
            result.Metrics?.Executions ?? 0,
            result.ElapsedMs,
            result.Trial.Stage,
            Neighborhood: null,
            trial.WindowIndex);
    }

    private static OptimizationTrialRunner TrialRunner(int workers)
    {
        return new OptimizationTrialRunner(new OptimizationTrialRunnerOptions { MaxWorkers = workers, ChannelCapacity = 1 }, TimeProvider.System);
    }

    private static WalkForwardRunner Runner(int workers)
    {
        return new WalkForwardRunner(TrialRunner(workers));
    }

    private static (int Index, OptimizationTrialStatus Status, decimal? Objective, SampleMetrics? Metrics, string? Failure) TrialKey(OptimizationTrialResult result)
    {
        return (result.Trial.Index, result.Status, result.ObjectiveValue, result.Metrics, result.FailureCode);
    }

    private static OptimizationRunInputInternal FaultingInput()
    {
        var graph = new StrategyGraph(
            [
                EngineGraphFixture.Node("close", parameters: new Dictionary<string, ParamValue> { ["field"] = EngineGraphFixture.Text("Close") }),
                EngineGraphFixture.Node(
                    "threshold",
                    "Constant",
                    new Dictionary<string, ParamValue>
                        { ["value"] = new DecimalOptimizeParamValue(Min: 1m, Max: 1m, Step: 1m) },
                    ValueType.Price),
                EngineGraphFixture.Node("subtract", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = EngineGraphFixture.Text("Subtract") }),
                EngineGraphFixture.Node("divide", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = EngineGraphFixture.Text("Divide") })
            ],
            [
                EngineGraphFixture.Wire("close", "subtract", "a"), EngineGraphFixture.Wire("threshold", "subtract", "b"),
                EngineGraphFixture.Wire("threshold", "divide", "a"), EngineGraphFixture.Wire("subtract", "divide", "b")
            ]);
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Create(graph);
        decimal[] prices = [2m, 2m, 2m, 1m, 2m, 2m, 2m, 2m, 2m, 2m, 2m, 2m];
        Candle[] candles = prices.Select((price, index) => EngineGraphFixture.Candle(index, price)).ToArray();

        return input with { History = new PreparedReplay(Timeframe.Parse("1h"), Array.Empty<Candle>(), candles), InSampleBars = candles.Length };
    }

    [TestCase(OptimizationSearchMethod.Random)]
    [TestCase(OptimizationSearchMethod.Tpe)]
    public async Task SeededSmaWindowsMatchIndependentTrainAndFreshTestReplays(OptimizationSearchMethod method)
    {
        OptimizationRunInputInternal input = method == OptimizationSearchMethod.Tpe
            ? OptimizationRunnerFixture.Sma(maxFast: 15, maxSlow: 16) with { Seed = 42, Method = method, Budget = 100 }
            : OptimizationRunnerFixture.Sma() with { Seed = 42, Method = method, Budget = 4 };
        var settings = new WalkForwardSettings(windows: 2, trainToTestRatio: 2);
        var delivered = new List<WalkForwardTrialResultInternal>();
        var emitted = new List<WalkForwardWindowResult>();
        WalkForwardRunResultInternal result = await Runner(workers: 1)
            .RunAsync(
                input,
                settings,
                trial =>
                {
                    delivered.Add(trial);

                    return ValueTask.CompletedTask;
                },
                window =>
                {
                    emitted.Add(window);

                    return ValueTask.CompletedTask;
                });

        result.IsCancelled.ShouldBeFalse();
        result.Windows.ShouldBe(emitted);
        result.Windows.Select(window => window.Window)
            .ShouldBe(
            [
                new WalkForwardWindow(Index: 0, TrainStart: 0, TrainCount: 4, TestStart: 4, TestCount: 2),
                new WalkForwardWindow(Index: 1, TrainStart: 2, TrainCount: 4, TestStart: 6, TestCount: 2)
            ]);
        result.CompletedTrials.ShouldBe(settings.Windows * input.Budget);
        result.TotalTrials.ShouldBe(settings.Windows * input.Budget);
        result.RejectedByTemplate.ShouldBe(delivered.Count(trial => trial.Result.Status == OptimizationTrialStatus.RejectedByTemplate));

        foreach (WalkForwardWindowResult window in result.Windows)
        {
            OptimizationRunResultInternal directTrain = await TrialRunner(workers: 1)
                .RunAsync(
                    input with
                    {
                        History = input.History.Window(window.Window.TrainStart, window.Window.TrainCount),
                        InSampleBars = window.Window.TrainCount,
                        Seed = OptimizationSeeds.Derive(input.Seed, window.Window.Index)
                    },
                    _ => ValueTask.CompletedTask);
            delivered.Where(trial => trial.WindowIndex == window.Window.Index)
                .Select(trial => (trial.Result.Trial.Index, trial.Result.Trial.GridIndex, trial.Result.Trial.Stage, trial.Result.Status, trial.Result.ObjectiveValue))
                .OrderBy(trial => trial.Index)
                .ShouldBe(
                    directTrain.Ranking.Select(trial => (trial.Trial.Index, trial.Trial.GridIndex, trial.Trial.Stage, trial.Status, trial.ObjectiveValue))
                        .OrderBy(trial => trial.Index));
            OptimizationTrialResult winner = directTrain.Ranking.First(trial => trial.Status == OptimizationTrialStatus.Completed);
            window.Status.ShouldBe(WalkForwardWindowStatus.Completed);
            window.WinnerTrialIndex.ShouldBe(winner.Trial.Index);
            window.Parameters.ShouldNotBeNull().OptimizedValues.ShouldBe(winner.Trial.Selections.OptimizedValues);
            window.TrainMetrics.ShouldBe(winner.Metrics);
            window.TrainFrom.ShouldBe(input.History.Replay[window.Window.TrainStart].OpenTime);
            window.TrainTo.ShouldBe(input.History.Replay[window.Window.TestStart - 1].OpenTime);
            window.TestFrom.ShouldBe(input.History.Replay[window.Window.TestStart].OpenTime);
            window.TestTo.ShouldBe(input.History.Replay[window.Window.TestStart + window.Window.TestCount - 1].OpenTime);
            var testInput = new BacktestInput(
                input.CompiledGraph,
                winner.Trial.Selections,
                input.Settings,
                input.Rules,
                input.History.Window(window.Window.TestStart, window.Window.TestCount),
                OutOfSampleSplit.None);
            var observer = new MetricsObserver(input.Settings.StartingCapital, testInput.SampleRanges);
            BacktestResult test = new BacktestEngine().Run(testInput, observer, options: new BacktestRunOptions { RetainLedger = false });
            test.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
            test.Ledger.ShouldBeEmpty();
            window.TestMetrics.ShouldBe(observer.Snapshot.Full);
            delivered.Where(trial => trial.WindowIndex == window.Window.Index)
                .Select(trial => TrialKey(trial.Result))
                .OrderBy(trial => trial.Index)
                .ShouldBe(directTrain.Ranking.Select(TrialKey).OrderBy(trial => trial.Index));
        }

        // Two closes cannot fill an SMA crossover order generated on the final close; each test keeps its fresh 1,000 capital.
        WalkForwardWindowResult first = result.Windows[0];
        first.TestMetrics.ShouldNotBeNull().TotalPnl.ShouldBe(0m);
        first.TestMetrics.EndEquity.ShouldBe(1000m);
        first.TestMetrics.ReturnPercent.ShouldBe(0m);
        first.TestMetrics.Executions.ShouldBe(0);
        first.Equity.ShouldBe(
        [
            new WalkForwardEquityPoint(CandleIndex: 4, CloseEquity: 1000m, input.History.Replay[4].OpenTime),
            new WalkForwardEquityPoint(CandleIndex: 5, CloseEquity: 1000m, input.History.Replay[5].OpenTime)
        ]);
        result.Summary.TestWindows.ShouldBe(2);
        result.Summary.TestTotalPnl.ShouldBe(0m);
        result.Summary.TestCompoundedReturnPercent.ShouldBe(0m);
    }

    [Test]
    public async Task AlreadyCancelledRunEmitsNeitherTrialsNorWindows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var delivered = 0;
        WalkForwardRunResultInternal result = await Runner(workers: 8)
            .RunAsync(
                OptimizationRunnerFixture.Sma(),
                new WalkForwardSettings(windows: 2, trainToTestRatio: 2),
                _ =>
                {
                    delivered++;

                    return ValueTask.CompletedTask;
                },
                _ => throw new InvalidOperationException("No window should be emitted."),
                cancellationToken: cancellation.Token);

        result.IsCancelled.ShouldBeTrue();
        delivered.ShouldBe(0);
        result.CompletedTrials.ShouldBe(0);
        result.Windows.ShouldBeEmpty();
    }

    [Test]
    public async Task CancellationDrainsTrialsButSuppressesCurrentWindow()
    {
        using var cancellation = new CancellationTokenSource();
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 20, maxSlow: 30) with { Budget = 100 };
        var delivered = new List<WalkForwardTrialResultInternal>();
        var windows = new List<WalkForwardWindowResult>();
        WalkForwardRunResultInternal result = await Runner(workers: 1)
            .RunAsync(
                input,
                new WalkForwardSettings(windows: 2, trainToTestRatio: 2),
                trial =>
                {
                    delivered.Add(trial);

                    if (delivered.Count(value => value.Result.Status != OptimizationTrialStatus.RejectedByTemplate) == 3)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                window =>
                {
                    windows.Add(window);

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);

        result.IsCancelled.ShouldBeTrue();
        result.CompletedTrials.ShouldBe(delivered.Count(trial => trial.Result.Status != OptimizationTrialStatus.RejectedByTemplate));
        result.CompletedTrials.ShouldBeGreaterThanOrEqualTo(3);
        result.CompletedTrials.ShouldBeLessThan(100);
        delivered.ShouldAllBe(trial => trial.WindowIndex == 0);
        result.Windows.ShouldBeEmpty();
        windows.ShouldBeEmpty();
    }

    [Test]
    public async Task CancellationInNextWindowPreservesPreviouslyEmittedTest()
    {
        using var cancellation = new CancellationTokenSource();
        var emitted = new List<WalkForwardWindowResult>();
        WalkForwardRunResultInternal result = await Runner(workers: 1)
            .RunAsync(
                OptimizationRunnerFixture.Sma(),
                new WalkForwardSettings(windows: 2, trainToTestRatio: 2),
                trial =>
                {
                    if (trial.WindowIndex == 1)
                    {
                        cancellation.Cancel();
                    }

                    return ValueTask.CompletedTask;
                },
                window =>
                {
                    emitted.Add(window);

                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellation.Token);

        result.IsCancelled.ShouldBeTrue();
        result.Windows.ShouldBe(emitted);
        result.Windows.Count.ShouldBe(1);
        result.Windows[0].Window.Index.ShouldBe(0);
        result.Windows[0].Status.ShouldBe(WalkForwardWindowStatus.Completed);
        result.Summary.TestWindows.ShouldBe(1);
    }

    [Test]
    public async Task CoarseToFineProgressStartsFromCoarseAndRefineBoundThenCorrectsAfterFirstWindow()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma(maxFast: 2, maxSlow: 4) with
        {
            Budget = 100,
            Method = OptimizationSearchMethod.CoarseToFine
        };
        var received = new List<OptimizationProgressInternal>();
        var progress = Substitute.For<IProgress<OptimizationProgressInternal>>();
        progress.When(reporter => reporter.Report(Arg.Any<OptimizationProgressInternal>()))
            .Do(call => received.Add(call.Arg<OptimizationProgressInternal>().ShouldNotBeNull()));

        WalkForwardRunResultInternal result = await Runner(workers: 2)
            .RunAsync(
                input,
                new WalkForwardSettings(windows: 2, trainToTestRatio: 2),
                _ => ValueTask.CompletedTask,
                _ => ValueTask.CompletedTask,
                progress);

        received[0].Total.ShouldBe(48);
        result.TotalTrials.ShouldBe(10);
        received.ShouldContain(item => item.Total == 10);
    }

    [Test]
    public async Task CombinedBudgetOverLimitFailsBeforeDeliveringTrials()
    {
        var error = await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Runner(workers: 1)
            .RunAsync(
                OptimizationRunnerFixture.Sma() with { Budget = 100000, InSampleBars = 12 },
                new WalkForwardSettings(windows: 11, trainToTestRatio: 1),
                _ => throw new InvalidOperationException("No trial should be emitted."),
                _ => ValueTask.CompletedTask));

        error.ParamName.ShouldBe("input");
    }

    [Test]
    public async Task DataIndependentCandidateLimitFailsTheWholeRun()
    {
        OptimizationRunInputInternal source = OptimizationRunnerFixture.Sma();
        var space = new OptimizationSearchSpace(
        [
            new OptimizationParameterRange(new PortReference("fastPeriod", "value"), min: 100m, max: 1000000000m, step: 1m, ValueType.Integer),
            new OptimizationParameterRange(new PortReference("slowPeriod", "value"), min: 2m, max: 3m, step: 1m, ValueType.Integer)
        ]);
        var trials = new List<WalkForwardTrialResultInternal>();
        var windows = new List<WalkForwardWindowResult>();
        var error = await Should.ThrowAsync<OptimizationException>(() => Runner(workers: 8)
            .RunAsync(
                source with { SearchSpace = space, Budget = 2 },
                new WalkForwardSettings(windows: 2, trainToTestRatio: 2),
                trial =>
                {
                    trials.Add(trial);

                    return ValueTask.CompletedTask;
                },
                window =>
                {
                    windows.Add(window);

                    return ValueTask.CompletedTask;
                }));

        error.Code.ShouldBe("candidate_limit_exceeded");
        windows.ShouldBeEmpty();
        trials.Count.ShouldBe(space.CandidateLimit(budget: 2));
        trials.ShouldAllBe(trial => trial.WindowIndex == 0 && trial.Result.Status == OptimizationTrialStatus.RejectedByTemplate);
    }

    [Test]
    public async Task FailedTestAndNoWinnerDoNotStopLaterSuccessfulWindow()
    {
        OptimizationRunInputInternal input = FaultingInput();
        WalkForwardRunResultInternal result = await Runner(workers: 8)
            .RunAsync(
                input,
                new WalkForwardSettings(windows: 3, trainToTestRatio: 1),
                _ => ValueTask.CompletedTask,
                _ => ValueTask.CompletedTask);

        result.Windows.Select(window => window.Status)
            .ShouldBe([WalkForwardWindowStatus.TestFailed, WalkForwardWindowStatus.NoWinner, WalkForwardWindowStatus.Completed]);
        WalkForwardWindowResult failed = result.Windows[0];
        failed.WinnerTrialIndex.ShouldBe(0);
        failed.TrainMetrics.ShouldNotBeNull();
        failed.TestMetrics.ShouldBeNull();
        failed.Equity.ShouldBeEmpty();
        var directInput = new BacktestInput(
            input.CompiledGraph,
            failed.Parameters.ShouldNotBeNull(),
            input.Settings,
            input.Rules,
            input.History.Window(failed.Window.TestStart, failed.Window.TestCount));
        failed.FailureCode.ShouldBe(new BacktestEngine().Run(directInput).Terminal.Diagnostic.ShouldNotBeNull().Code);
        result.Summary.TestWindows.ShouldBe(1);
        result.Summary.TestTotalPnl.ShouldBe(0m);
    }

    [Test]
    public async Task GridBelowBudgetAndTemplateRejectionsFinishAtTheRunnableTotal()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma() with { Budget = 100 };
        var settings = new WalkForwardSettings(windows: 2, trainToTestRatio: 2);
        var reported = new List<OptimizationProgressInternal>();
        var progress = Substitute.For<IProgress<OptimizationProgressInternal>>();
        progress.When(value => value.Report(Arg.Any<OptimizationProgressInternal>()))
            .Do(call => reported.Add(call.Arg<OptimizationProgressInternal>().ShouldNotBeNull()));

        WalkForwardRunResultInternal result = await Runner(workers: 1)
            .RunAsync(input, settings, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, progress);

        reported[0].Total.ShouldBe(18);
        result.RejectedByTemplate.ShouldBe(6);
        result.CompletedTrials.ShouldBe(12);
        result.TotalTrials.ShouldBe(12);
        reported[^1].ShouldBe(new OptimizationProgressInternal(Completed: 12, Total: 12));
    }

    [Test]
    public async Task NoEligibleTrialProducesNoWinnerAndContinuesEveryWindow()
    {
        OptimizationRunInputInternal input = OptimizationRunnerFixture.Sma() with { Constraints = new OptimizationConstraints(minExecutions: 100) };
        WalkForwardRunResultInternal result = await Runner(workers: 8)
            .RunAsync(
                input,
                new WalkForwardSettings(windows: 2, trainToTestRatio: 2),
                _ => ValueTask.CompletedTask,
                _ => ValueTask.CompletedTask);

        result.Windows.Count.ShouldBe(2);
        result.Windows.ShouldAllBe(window => window.Status == WalkForwardWindowStatus.NoWinner);
        result.Windows.ShouldAllBe(window => window.WinnerTrialIndex == null && window.Parameters == null && window.TestMetrics == null && window.Equity.Count == 0);
        result.Summary.TestWindows.ShouldBe(0);
        result.CompletedTrials.ShouldBe(12);
    }

    [Test]
    public async Task ReceiverFailurePropagatesWithoutEmittingAWindow()
    {
        var error = await Should.ThrowAsync<InvalidOperationException>(() => Runner(workers: 8)
            .RunAsync(
                OptimizationRunnerFixture.Sma(),
                new WalkForwardSettings(windows: 2, trainToTestRatio: 2),
                _ => throw new InvalidOperationException("trial sink failed"),
                _ => ValueTask.CompletedTask)
            .WaitAsync(TimeSpan.FromSeconds(10)));

        error.Message.ShouldBe("trial sink failed");
    }
}