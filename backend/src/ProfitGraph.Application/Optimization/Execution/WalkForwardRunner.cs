using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Optimization.Execution;

/// <summary>
///     Searches train windows sequentially and replays each eligible winner with a fresh test account (ADR 0014). Windows
///     an earlier attempt finished are kept, and the first unfinished one continues from its stored trials.
/// </summary>
public sealed class WalkForwardRunner(OptimizationTrialRunner trials)
{
    private static readonly string RejectedByTemplate = OptimizationNames.TrialStatus(OptimizationTrialStatus.RejectedByTemplate);

    public async Task<WalkForwardRunResultInternal> RunAsync(
        OptimizationRunInputInternal input,
        WalkForwardSettings settings,
        Func<WalkForwardTrialResultInternal, ValueTask> trialSink,
        Func<WalkForwardWindowResult, ValueTask> windowSink,
        IProgress<OptimizationProgressInternal>? progress = null,
        CancellationToken cancellationToken = default,
        Func<int, IReadOnlyDictionary<int, OptimizationNeighborhoodScore>, ValueTask>? scoreSink = null,
        Func<ValueTask>? idleFlush = null,
        IReadOnlyList<OptimizationTrialInternal>? persistedTrials = null,
        IReadOnlyList<WalkForwardWindowResult>? completedWindows = null)
    {
        var plan = WalkForwardPlan.Create(input.InSampleBars, settings);

        if (input.Budget is < 1 or > OptimizationSearchSpace.MaximumBudget || !Enum.IsDefined(input.Objective))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Budget and objective must be within the supported limits.");
        }

        int plannedPerWindow = input.SearchSpace.GridSize < input.Budget ? (int)input.SearchSpace.GridSize : input.Budget;

        if (input.Method == OptimizationSearchMethod.CoarseToFine)
        {
            if (!OptimizationCoarseToFinePlan.TryCreate(input.SearchSpace, input.Budget, out OptimizationCoarseToFinePlan? coarsePlan))
            {
                throw new OptimizationException(OptimizationErrorCodes.MethodNotApplicable, "Coarse-to-fine search is not applicable to this search space and budget.");
            }

            var coarseSize = (int)coarsePlan!.CoarseGridSize;
            plannedPerWindow = checked(coarseSize + Math.Min(input.Budget - coarseSize, coarsePlan.RefineCandidateUpperBound));
        }

        int total = checked(settings.Windows * plannedPerWindow);

        if ((long)settings.Windows * input.Budget > OptimizationSearchSpace.MaximumBudget)
        {
            throw new ArgumentOutOfRangeException(nameof(input), $"Combined walk-forward budget must not exceed {OptimizationSearchSpace.MaximumBudget} trials.");
        }

        // Validate the frozen replay boundary even when cancellation was requested before this invocation.
        input.History.Slice(input.InSampleBars);
        IReadOnlyList<OptimizationTrialInternal> storedTrials = persistedTrials ?? [];
        Dictionary<int, WalkForwardWindowResult> storedWindows = (completedWindows ?? []).ToDictionary(result => result.Window.Index);
        var windows = new List<WalkForwardWindowResult>();
        int completed = storedTrials.Count(trial => trial.Status != RejectedByTemplate);
        int rejected = storedTrials.Count - completed;

        if (storedWindows.ContainsKey(0))
        {
            total = checked(settings.Windows * storedTrials.Count(trial => trial.WindowIndex == 0 && trial.Status != RejectedByTemplate));
        }

        progress?.Report(new OptimizationProgressInternal(completed, total));

        foreach (WalkForwardWindow window in plan.Windows)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (storedWindows.TryGetValue(window.Index, out WalkForwardWindowResult? storedWindow))
            {
                windows.Add(storedWindow);

                continue;
            }

            OptimizationTrialInternal[] windowTrials = storedTrials.Where(trial => trial.WindowIndex == window.Index).ToArray();
            OptimizationRunInputInternal trainInput = input with
            {
                History = input.History.Window(window.TrainStart, window.TrainCount),
                InSampleBars = window.TrainCount,
                Seed = OptimizationSeeds.Derive(input.Seed, window.Index)
            };
            OptimizationRunResultInternal train = await trials.RunAsync(
                trainInput,
                async result =>
                {
                    await trialSink(new WalkForwardTrialResultInternal(window.Index, result));

                    if (result.Status == OptimizationTrialStatus.RejectedByTemplate)
                    {
                        rejected++;
                    }
                    else
                    {
                        completed++;
                        progress?.Report(new OptimizationProgressInternal(completed, total));
                    }
                },
                cancellationToken: cancellationToken,
                idleFlush: idleFlush,
                persistedTrials: windowTrials);

            if (train.IsCancelled || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (window.Index == 0)
            {
                int runnableTotal = checked(settings.Windows * train.TotalTrials);

                if (runnableTotal != total)
                {
                    total = runnableTotal;
                    progress?.Report(new OptimizationProgressInternal(completed, total));
                }
            }

            if (scoreSink is not null)
            {
                await scoreSink(window.Index, train.NeighborhoodScores);
            }

            OptimizationTrialResult? winner = train.Ranking.FirstOrDefault(result => result.Status == OptimizationTrialStatus.Completed);
            WalkForwardWindowResult result;

            if (winner is not null && windowTrials.Any(trial => trial.TrialIndex == winner.Trial.Index))
            {
                // Persisted trial columns are sufficient for ranking; only the selected train needs full metrics.
                var trainReplay = new BacktestInput(input.CompiledGraph, winner.Trial.Selections, input.Settings, input.Rules, trainInput.History);
                var trainObserver = new MetricsObserver(input.Settings.StartingCapital, trainReplay.SampleRanges);
                BacktestResult replayedTrain = new BacktestEngine().Run(
                    trainReplay,
                    trainObserver,
                    cancellationToken,
                    new BacktestRunOptions { RetainLedger = false });

                if (replayedTrain.Terminal.Outcome == BacktestOutcome.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (replayedTrain.Terminal.Outcome != BacktestOutcome.Completed)
                {
                    throw new OptimizationException(OptimizationErrorCodes.PersistedTrialMismatch, "A stored winning trial failed when replayed for its metrics.");
                }

                winner = OptimizationTrialResult.Restore(
                    winner.Trial,
                    trainObserver.Snapshot.InSample,
                    winner.Status,
                    winner.ObjectiveValue,
                    winner.FailureCode,
                    winner.ElapsedMs);
            }

            if (winner is null)
            {
                result = WindowResult(
                    window,
                    winner: null,
                    testMetrics: null,
                    WalkForwardWindowStatus.NoWinner,
                    failureCode: null,
                    []);
            }
            else
            {
                var testInput = new BacktestInput(
                    input.CompiledGraph,
                    winner.Trial.Selections,
                    input.Settings,
                    input.Rules,
                    input.History.Window(window.TestStart, window.TestCount),
                    OutOfSampleSplit.None);
                var observer = new WalkForwardEquityObserver(input.Settings.StartingCapital, testInput.SampleRanges, window.TestStart, testInput.Replay);
                BacktestResult test = new BacktestEngine().Run(
                    testInput,
                    observer,
                    cancellationToken,
                    new BacktestRunOptions { RetainLedger = false });

                if (test.Terminal.Outcome == BacktestOutcome.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                result = test.Terminal.Outcome == BacktestOutcome.Failed
                    ? WindowResult(
                        window,
                        winner,
                        testMetrics: null,
                        WalkForwardWindowStatus.TestFailed,
                        test.Terminal.Diagnostic!.Code,
                        [])
                    : WindowResult(window, winner, observer.Snapshot.Full, WalkForwardWindowStatus.Completed, failureCode: null, observer.Equity);
            }

            await windowSink(result);
            windows.Add(result);
        }

        IReadOnlyList<WalkForwardWindowResult> frozenWindows = windows.AsReadOnly();

        return new WalkForwardRunResultInternal(
            frozenWindows,
            WalkForwardSummaryCalculator.Build(frozenWindows, input.SearchSpace),
            completed,
            total,
            rejected,
            cancellationToken.IsCancellationRequested);

        WalkForwardWindowResult WindowResult(
            WalkForwardWindow window,
            OptimizationTrialResult? winner,
            SampleMetrics? testMetrics,
            WalkForwardWindowStatus status,
            string? failureCode,
            IReadOnlyList<WalkForwardEquityPoint> equity) => new WalkForwardWindowResult(
            window,
            input.History.Replay[window.TrainStart].OpenTime,
            input.History.Replay[checked(window.TrainStart + window.TrainCount - 1)].OpenTime,
            input.History.Replay[window.TestStart].OpenTime,
            input.History.Replay[checked(window.TestStart + window.TestCount - 1)].OpenTime,
            winner?.Trial.Index,
            winner?.Trial.Selections,
            winner?.Metrics,
            testMetrics,
            status,
            failureCode,
            equity);
    }
}