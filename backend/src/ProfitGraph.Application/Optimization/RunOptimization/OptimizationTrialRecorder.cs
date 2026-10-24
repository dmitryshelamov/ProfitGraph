using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Execution;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Optimization.RunOptimization;

/// <summary>
///     Stores the trials the runner delivers in batches, each in one short transaction with the optimization's counters,
///     plan and best trial, and reports job progress after each commit. New trials continue the stored delivery sequence.
/// </summary>
internal sealed class OptimizationTrialRecorder
{
    private readonly List<(int Window, OptimizationTrialResult Result)> _batch;
    private readonly int _batchSize;
    private readonly IOptimizationRunRepository _optimizations;
    private readonly OptimizationRun _optimization;
    private readonly IBackgroundJobProgress _progress;
    private readonly bool _ranksBest;
    private readonly IOptimizationTrialStore _trials;
    private readonly ITransactionExecutor _transactions;
    private OptimizationTrialResult? _best;
    private long _sequence;

    /// <summary>
    ///     The planned total the runner reports; it may change as the search learns it.
    /// </summary>
    public OptimizationPlannedTrials Planned { get; } = new OptimizationPlannedTrials();

    // ranksBest: whether the optimization keeps a best trial; walk-forward windows each have their own winner instead.
    public OptimizationTrialRecorder(
        OptimizationRun optimization,
        IReadOnlyList<OptimizationTrialInternal> stored,
        bool ranksBest,
        int batchSize,
        IOptimizationTrialStore trials,
        IOptimizationRunRepository optimizations,
        ITransactionExecutor transactions,
        IBackgroundJobProgress progress)
    {
        _optimization = optimization;
        _ranksBest = ranksBest;
        _batchSize = batchSize;
        _trials = trials;
        _optimizations = optimizations;
        _transactions = transactions;
        _progress = progress;
        _batch = new List<(int Window, OptimizationTrialResult Result)>(batchSize);
        _sequence = stored.Count == 0 ? 0 : stored.Max(trial => trial.Sequence);
        _best = ranksBest
            ? OptimizationRanking.Rank(stored.Where(trial => trial is { Status: "completed", Objective: not null }).Select(Ranked)).FirstOrDefault()
            : null;
    }

    public async ValueTask Add(int windowIndex, OptimizationTrialResult trial)
    {
        _batch.Add((windowIndex, trial));

        if (_batch.Count >= _batchSize)
        {
            await Flush();
        }
    }

    public async ValueTask Flush()
    {
        if (_batch.Count == 0)
        {
            return;
        }

        OptimizationTrialInternal[] records = [.. _batch.Select(item => Record(++_sequence, item.Window, item.Result))];

        if (_ranksBest)
        {
            _best = OptimizationRanking.Rank(
                    _batch.Select(item => item.Result)
                        .Where(trial => trial is { Status: OptimizationTrialStatus.Completed, ObjectiveValue: not null })
                        .Append(_best)
                        .OfType<OptimizationTrialResult>())
                .FirstOrDefault();
        }

        if (Planned.Total is { } total && total != _optimization.PlannedTrials && total >= _optimization.Counters.Executed + _batch.Count)
        {
            _optimization.PlanTrials(total);
        }

        _optimization.RecordTrials(_batch.Select(item => item.Result), _best is null ? null : new OptimizationBestTrial(_best.Trial.Index, _best.ObjectiveValue!.Value));
        await _transactions.ExecuteAsync(
            async () =>
            {
                await _trials.Write(_optimization.Id, records, CancellationToken.None);
                await _optimizations.Save(_optimization, CancellationToken.None);

                return true;
            },
            CancellationToken.None);
        _batch.Clear();
        await _progress.RecordStoredWork(CancellationToken.None);
        await _progress.Report(
            _optimization.Counters.Executed,
            _optimization.PlannedTrials ?? _optimization.Configuration.TrialCapacity,
            CancellationToken.None);
    }

    /// <summary>
    ///     A stored completed trial as a rankable result: ranking reads only its objective and its values.
    /// </summary>
    private static OptimizationTrialResult Ranked(OptimizationTrialInternal stored)
    {
        var trial = new OptimizationTrial(
            stored.TrialIndex,
            GridIndex: 0,
            new EngineExecutionInputs(stored.Parameters.ToDictionary(pair => new PortReference(pair.Key, "value"), pair => pair.Value)));

        return OptimizationTrialResult.Restore(trial, metrics: null, OptimizationTrialStatus.Completed, stored.Objective, failureCode: null, stored.ElapsedMs);
    }

    private static OptimizationTrialInternal Record(long sequence, int windowIndex, OptimizationTrialResult trial)
    {
        return new OptimizationTrialInternal(
            sequence,
            trial.Trial.Index,
            trial.Trial.Selections.OptimizedValues.ToDictionary(pair => pair.Key.NodeId, pair => pair.Value, StringComparer.Ordinal),
            OptimizationNames.TrialStatus(trial.Status),
            trial.FailureCode,
            trial.ObjectiveValue,
            trial.Metrics?.ReturnPercent,
            trial.Metrics?.TotalPnl,
            trial.Metrics?.MaxDrawdownPercent,
            trial.Metrics?.Executions ?? 0,
            trial.ElapsedMs,
            trial.Trial.Stage,
            Neighborhood: null,
            windowIndex);
    }
}