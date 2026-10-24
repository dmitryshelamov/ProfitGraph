using System.Diagnostics;
using System.Threading.Channels;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Optimization.Execution;

/// <summary>
///     Runs isolated metrics-only trials on the in-sample slice and drains completed results through one bounded, serial
///     receiver. The ranking does not depend on the worker count or the order in which trials finish. Trials stored by an
///     earlier attempt are restored instead of replayed, so a resumed search ends exactly as an uninterrupted one.
/// </summary>
public sealed class OptimizationTrialRunner
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
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
    /// <param name="idleFlush">Called on the same consumer at least once a second, so a receiver can commit a partial batch.</param>
    /// <param name="persistedTrials">Trials an earlier attempt stored; they are restored, counted and ranked but not delivered.</param>
    public async Task<OptimizationRunResultInternal> RunAsync(
        OptimizationRunInputInternal input,
        Func<OptimizationTrialResult, ValueTask> sink,
        IProgress<OptimizationProgressInternal>? progress = null,
        CancellationToken cancellationToken = default,
        Func<ValueTask>? idleFlush = null,
        IReadOnlyList<OptimizationTrialInternal>? persistedTrials = null)
    {
        if (input.Budget is < 1 or > OptimizationSearchSpace.MaximumBudget || !Enum.IsDefined(input.Objective) ||
            input.Method is not (OptimizationSearchMethod.Grid or OptimizationSearchMethod.Random))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Budget must be within the supported limit and the objective and method must be supported.");
        }

        PreparedReplay history = input.History.Slice(input.InSampleBars);
        OptimizationConstraints constraints = input.Constraints ?? new OptimizationConstraints();
        var results = new List<OptimizationTrialResult>();
        var channel = Channel.CreateBounded<OptimizationTrialResult>(
            new BoundedChannelOptions(_options.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        using var workersCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Dictionary<int, OptimizationTrialInternal> persisted = (persistedTrials ?? []).ToDictionary(trial => trial.TrialIndex);
        var total = 0;
        int rejectedByTemplate = persisted.Values.Count(trial => trial.Status == OptimizationNames.TrialStatus(OptimizationTrialStatus.RejectedByTemplate));
        int completed = persisted.Count - rejectedByTemplate;
        var progressStarted = false;
        Task producer = ProduceAsync();

        try
        {
            await foreach (OptimizationTrialResult result in OptimizationResultStream.Read(channel.Reader, idleFlush, FlushInterval, _clock))
            {
                bool restored = persisted.ContainsKey(result.Trial.Index);

                if (!restored)
                {
                    await sink(result);
                }

                results.Add(result);

                if (result.Status == OptimizationTrialStatus.RejectedByTemplate)
                {
                    rejectedByTemplate += restored ? 0 : 1;

                    continue;
                }

                // Rejections stream while candidates are filtered, so progress starts once the planned total is known.
                if (!progressStarted)
                {
                    progress?.Report(new OptimizationProgressInternal(completed, total));
                    progressStarted = true;
                }

                completed += restored ? 0 : 1;
                progress?.Report(new OptimizationProgressInternal(completed, total));
            }

            await producer;

            if (!progressStarted)
            {
                progress?.Report(new OptimizationProgressInternal(completed, total));
            }
        }
        catch
        {
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

        return new OptimizationRunResultInternal(OptimizationRanking.Rank(results), completed, total, rejectedByTemplate, cancellationToken.IsCancellationRequested);

        async Task ProduceAsync()
        {
            try
            {
                List<OptimizationTrial> runnable = await FilterAsync(
                    input.SearchSpace.EnumerateCandidates(input.Budget, input.Seed, input.Method == OptimizationSearchMethod.Random),
                    input.Budget);

                if (runnable.Count < input.Budget && input.SearchSpace.GridSize > input.SearchSpace.CandidateLimit(input.Budget))
                {
                    throw new OptimizationException(
                        OptimizationErrorCodes.CandidateLimitExceeded,
                        $"Examined {input.SearchSpace.CandidateLimit(input.Budget)} candidates without finding {input.Budget} template-valid trials. " +
                        "Narrow the parameter ranges or reduce the requested budget.");
                }

                total = runnable.Count;
                await RunWorkersAsync(runnable);
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

        async Task<List<OptimizationTrial>> FilterAsync(IEnumerable<OptimizationTrial> candidates, int limit)
        {
            var runnable = new List<OptimizationTrial>();

            foreach (OptimizationTrial trial in candidates)
            {
                workersCancellation.Token.ThrowIfCancellationRequested();

                if (TemplateParameterRules.Check(input.Template, input.CompiledGraph, trial.Selections).Count > 0)
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

        async Task RunWorkersAsync(IReadOnlyList<OptimizationTrial> runnable)
        {
            await Parallel.ForEachAsync(
                runnable,
                new ParallelOptions { MaxDegreeOfParallelism = _options.MaxWorkers, CancellationToken = workersCancellation.Token },
                async (trial, token) =>
                {
                    if (persisted.TryGetValue(trial.Index, out OptimizationTrialInternal? stored))
                    {
                        await channel.Writer.WriteAsync(
                            OptimizationTrialRestoring.Restore(trial, stored, input.InSampleBars, rejectedByTemplate: false),
                            CancellationToken.None);

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
                });
        }
    }
}