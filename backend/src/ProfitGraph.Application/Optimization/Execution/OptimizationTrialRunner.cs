using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Channels;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Optimization.Search;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Optimization.Execution;

/// <summary>
///     Runs isolated metrics-only trials on the in-sample slice and drains completed results through one bounded, serial
///     receiver. The ranking does not depend on the worker count or the order in which trials finish. Trials stored by an
///     earlier attempt are restored instead of replayed, so a resumed search ends exactly as an uninterrupted one. A
///     coarse-to-fine search refines after its whole first stage is received; TPE guides each batch by the trials the
///     receiver accepted (Profit Fox Designer ADR 0033).
/// </summary>
public sealed class OptimizationTrialRunner
{
    private readonly TimeProvider _clock;
    private readonly OptimizationTrialRunnerOptions _options;

    public OptimizationTrialRunner(OptimizationTrialRunnerOptions options, TimeProvider clock)
    {
        if (!options.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Worker count and channel capacity must be positive.");
        }

        _clock = clock;
        _options = options;
    }

    /// <param name="input">The frozen search: graph, history, settings, space, budget, seed and method.</param>
    /// <param name="sink">Receives every delivered trial on one consumer, template rejections included.</param>
    /// <param name="progress">Executed trials against the planned total, reported after each delivered execution.</param>
    /// <param name="cancellationToken">Stops new trials; results already produced are still delivered.</param>
    /// <param name="idleFlush">
    ///     Called on the same consumer at least once per <see cref="OptimizationTrialRunnerOptions.FlushInterval" />, so a
    ///     receiver can commit a partial batch.
    /// </param>
    /// <param name="persistedTrials">Trials an earlier attempt stored; they are restored, counted and ranked but not delivered.</param>
    public async Task<OptimizationRunResultInternal> RunAsync(
        OptimizationRunInputInternal input,
        Func<OptimizationTrialResult, ValueTask> sink,
        IProgress<OptimizationProgressInternal>? progress = null,
        CancellationToken cancellationToken = default,
        Func<ValueTask>? idleFlush = null,
        IReadOnlyList<OptimizationTrialInternal>? persistedTrials = null)
    {
        if (input.Budget is < 1 or > OptimizationSearchSpace.MaximumBudget || !Enum.IsDefined(input.Objective) || !Enum.IsDefined(input.Method))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Budget must be within the supported limit and the objective and method must be supported.");
        }

        OptimizationCoarseToFinePlan? coarsePlan = null;

        if (input.Method == OptimizationSearchMethod.CoarseToFine && !OptimizationCoarseToFinePlan.TryCreate(input.SearchSpace, input.Budget, out coarsePlan))
        {
            throw new OptimizationException(OptimizationErrorCodes.MethodNotApplicable, "Coarse-to-fine search is not applicable to this search space and budget.");
        }

        OptimizationTpePlan? tpePlan = null;

        if (input.Method == OptimizationSearchMethod.Tpe && !OptimizationTpePlan.TryCreate(input.SearchSpace, input.Budget, out tpePlan))
        {
            throw new OptimizationException(OptimizationErrorCodes.MethodNotApplicable, "TPE search is not applicable to this search space and budget.");
        }

        PreparedReplay history = input.History.Slice(input.InSampleBars);
        OptimizationConstraints constraints = input.Constraints ?? new OptimizationConstraints();
        Dictionary<int, OptimizationTrialInternal> persisted = (persistedTrials ?? []).ToDictionary(trial => trial.TrialIndex);
        var results = new List<OptimizationTrialResult>();
        var channel = Channel.CreateBounded<OptimizationTrialResult>(
            new BoundedChannelOptions(_options.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        using var workersCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var total = 0;
        int rejectedByTemplate = persisted.Values.Count(trial => trial.Status == OptimizationNames.TrialStatus(OptimizationTrialStatus.RejectedByTemplate));
        int completed = persisted.Count - rejectedByTemplate;
        var progressStarted = false;
        int reportedTotal = -1;
        var stageOneReceived = 0;
        var stageOneExpected = 0;
        var stageOneDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var tpeReceived = new SemaphoreSlim(initialCount: 0);
        Task producer = ProduceAsync();

        try
        {
            await foreach (OptimizationTrialResult result in OptimizationResultStream.Read(channel.Reader, idleFlush, _options.FlushInterval, _clock))
            {
                bool restored = persisted.ContainsKey(result.Trial.Index);

                if (!restored)
                {
                    await sink(result);
                }

                results.Add(result);

                if (tpePlan is not null)
                {
                    tpeReceived.Release();
                }

                if (coarsePlan is not null && result.Trial.Stage == 1 && ++stageOneReceived == stageOneExpected)
                {
                    stageOneDrained.TrySetResult();
                }

                if (result.Status == OptimizationTrialStatus.RejectedByTemplate)
                {
                    rejectedByTemplate += restored ? 0 : 1;

                    continue;
                }

                // Rejections stream while candidates are filtered, so progress starts once the planned total is known.
                if (!progressStarted)
                {
                    Report();
                    progressStarted = true;
                }

                completed += restored ? 0 : 1;
                Report();
            }

            await producer;

            if (!progressStarted || reportedTotal != total)
            {
                Report();
            }
        }
        catch
        {
            stageOneDrained.TrySetCanceled();
            await workersCancellation.CancelAsync();
            channel.Writer.TryComplete();

            try
            {
                await producer;
            }
            catch
            {
                // Preserve the receiver or production error that interrupted the consumer.
            }

            throw;
        }

        bool cancelled = cancellationToken.IsCancellationRequested;
        IReadOnlyDictionary<int, OptimizationNeighborhoodScore> scores = cancelled
            ? new Dictionary<int, OptimizationNeighborhoodScore>()
            : OptimizationNeighborhoodScores.Calculate(input.SearchSpace, results, coarsePlan?.Multipliers);

        return new OptimizationRunResultInternal(OptimizationRanking.Rank(results), completed, total, rejectedByTemplate, cancelled, scores);

        void Report()
        {
            progress?.Report(new OptimizationProgressInternal(completed, Math.Max(completed, total)));
            reportedTotal = total;
        }

        async Task ProduceAsync()
        {
            try
            {
                if (tpePlan is not null)
                {
                    await RunTpeAsync(tpePlan);
                }
                else if (coarsePlan is null)
                {
                    List<OptimizationTrial> runnable = await FilterAsync(
                        input.SearchSpace.EnumerateCandidates(input.Budget, input.Seed, input.Method == OptimizationSearchMethod.Random),
                        input.Budget);

                    if (runnable.Count < input.Budget && input.SearchSpace.GridSize > input.SearchSpace.CandidateLimit(input.Budget))
                    {
                        throw CandidateLimitExceeded(input.SearchSpace.CandidateLimit(input.Budget), input.Budget);
                    }

                    total = runnable.Count;
                    await RunWorkersAsync(runnable, resultsSink: null);
                }
                else
                {
                    OptimizationTrial[] stageOneCandidates = [.. coarsePlan.StageOneCandidates()];
                    var visited = new HashSet<BigInteger>(stageOneCandidates.Select(trial => trial.GridIndex));
                    var stageOneResults = new ConcurrentBag<OptimizationTrialResult>();
                    stageOneExpected = stageOneCandidates.Length;
                    List<OptimizationTrial> stageOneRunnable = await FilterAsync(stageOneCandidates, input.Budget);
                    total = checked(stageOneRunnable.Count + Math.Min(input.Budget - stageOneRunnable.Count, coarsePlan.RefineCandidateUpperBound));

                    if (stageOneExpected == 0)
                    {
                        stageOneDrained.TrySetResult();
                    }

                    await RunWorkersAsync(stageOneRunnable, stageOneResults);

                    // Refinement starts from the whole first stage as the receiver accepted it, restored trials included.
                    await stageOneDrained.Task.WaitAsync(workersCancellation.Token);
                    workersCancellation.Token.ThrowIfCancellationRequested();
                    IReadOnlyList<OptimizationTrial> stageTwoCandidates = coarsePlan.StageTwoCandidates(stageOneResults, visited, stageOneCandidates.Length);
                    List<OptimizationTrial> stageTwoRunnable = await FilterAsync(stageTwoCandidates, input.Budget - stageOneRunnable.Count);
                    total = checked(stageOneRunnable.Count + stageTwoRunnable.Count);
                    await RunWorkersAsync(stageTwoRunnable, resultsSink: null);
                }

                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (workersCancellation.IsCancellationRequested)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);

                throw;
            }
        }

        async Task RunTpeAsync(OptimizationTpePlan plan)
        {
            total = input.Budget;
            var visited = new HashSet<BigInteger>();
            var nextIndex = 0;
            var evaluated = 0;
            int candidateLimit = input.SearchSpace.CandidateLimit(input.Budget);
            var batchIndex = 0;
            IEnumerable<OptimizationTrial> candidates = plan.StartupCandidates(input.Seed);
            int batchLimit = plan.StartupCount;

            while (evaluated < input.Budget)
            {
                workersCancellation.Token.ThrowIfCancellationRequested();
                int previousIndex = nextIndex;
                List<OptimizationTrial> runnable = await FilterAsync(TrackCandidates(candidates), batchLimit);
                await RunWorkersAsync(runnable, resultsSink: null);

                // The model sees only results accepted by the serial receiver, including restored trials.
                for (int received = previousIndex; received < nextIndex; received++)
                {
                    await tpeReceived.WaitAsync(workersCancellation.Token);
                }

                workersCancellation.Token.ThrowIfCancellationRequested();
                evaluated += runnable.Count;

                if (evaluated < input.Budget && nextIndex >= candidateLimit && input.SearchSpace.GridSize > candidateLimit)
                {
                    throw CandidateLimitExceeded(candidateLimit, input.Budget);
                }

                if (runnable.Count < batchLimit)
                {
                    break;
                }

                batchLimit = Math.Min(plan.BatchSize, input.Budget - evaluated);
                candidates = plan.GuidedCandidates(results, visited, batchIndex++, nextIndex, input.Seed);
            }

            if (persisted.Keys.Any(index => index >= nextIndex))
            {
                throw new OptimizationException(OptimizationErrorCodes.PersistedTrialMismatch, "A stored trial does not match the frozen search candidate at its index.");
            }

            IEnumerable<OptimizationTrial> TrackCandidates(IEnumerable<OptimizationTrial> source)
            {
                foreach (OptimizationTrial trial in source)
                {
                    if (nextIndex >= candidateLimit)
                    {
                        yield break;
                    }

                    visited.Add(trial.GridIndex);
                    nextIndex = checked(trial.Index + 1);

                    yield return trial;
                }
            }
        }

        async Task<List<OptimizationTrial>> FilterAsync(IEnumerable<OptimizationTrial> candidates, int limit)
        {
            var runnable = new List<OptimizationTrial>();

            if (limit == 0)
            {
                return runnable;
            }

            foreach (OptimizationTrial trial in candidates)
            {
                workersCancellation.Token.ThrowIfCancellationRequested();

                // A grid whose profit step cannot pay its fees is rejected like a template rule, without spending budget (ADR 0058).
                if (TemplateParameterRules.Check(input.Template, input.CompiledGraph, trial.Selections).Count > 0 ||
                    GridFeeCheck.Check(input.CompiledGraph, trial.Selections, input.Settings, input.Rules, input.History.Replay[0].Open).Any(finding => finding.IsError))
                {
                    await channel.Writer.WriteAsync(
                        persisted.TryGetValue(trial.Index, out OptimizationTrialInternal? stored)
                            ? OptimizationTrialRestoring.Restore(trial, stored, input.InSampleBars, rejectedByTemplate: true)
                            : OptimizationTrialResult.RejectedByTemplate(trial),
                        CancellationToken.None);

                    continue;
                }

                runnable.Add(trial);

                if (runnable.Count == limit)
                {
                    break;
                }
            }

            return runnable;
        }

        async Task RunWorkersAsync(IReadOnlyList<OptimizationTrial> runnable, ConcurrentBag<OptimizationTrialResult>? resultsSink)
        {
            await Parallel.ForEachAsync(
                runnable,
                new ParallelOptions { MaxDegreeOfParallelism = _options.MaxWorkers, CancellationToken = workersCancellation.Token },
                async (trial, token) =>
                {
                    if (persisted.TryGetValue(trial.Index, out OptimizationTrialInternal? stored))
                    {
                        OptimizationTrialResult restored = OptimizationTrialRestoring.Restore(trial, stored, input.InSampleBars, rejectedByTemplate: false);
                        await channel.Writer.WriteAsync(restored, CancellationToken.None);
                        resultsSink?.Add(restored);

                        return;
                    }

                    OptimizationTrialResult result;
                    var timer = Stopwatch.StartNew();

                    try
                    {
                        var backtestInput = new BacktestInput(input.CompiledGraph, trial.Selections, input.Settings, input.Rules, history);
                        var observer = new MetricsObserver(input.Settings.StartingCapital, backtestInput.SampleRanges);
                        BacktestResult backtest = new BacktestEngine().Run(backtestInput, observer, token, new BacktestRunOptions { RetainLedger = false });

                        if (backtest.Terminal.Outcome == BacktestOutcome.Cancelled)
                        {
                            return;
                        }

                        result = backtest.Terminal.Outcome == BacktestOutcome.Failed
                            ? OptimizationTrialResult.Failed(trial, backtest.Terminal.Diagnostic!.Code, timer.ElapsedMilliseconds)
                            : OptimizationTrialResult.Evaluate(trial, observer.Snapshot.InSample, input.Objective, constraints, timer.ElapsedMilliseconds);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        result = OptimizationTrialResult.Failed(trial, OptimizationErrorCodes.TrialException, timer.ElapsedMilliseconds);
                    }

                    // Accepted results survive user cancellation and backpressure until the receiver drains them.
                    await channel.Writer.WriteAsync(result, CancellationToken.None);
                    resultsSink?.Add(result);
                });
        }
    }

    private static OptimizationException CandidateLimitExceeded(int limit, int budget)
    {
        return new OptimizationException(
            OptimizationErrorCodes.CandidateLimitExceeded,
            $"Examined {limit} candidates without finding {budget} template-valid trials. Narrow the parameter ranges or reduce the requested budget.");
    }
}