using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Ports;

/// <summary>
///     Stores trial batches in the caller's transaction, where a trial already stored under its sequence is left unchanged,
///     and reads them back for a resumed search.
/// </summary>
public interface IOptimizationTrialStore
{
    Task Write(OptimizationId id, IReadOnlyList<OptimizationTrialInternal> trials, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores the neighborhood scores of a finished search, or of one walk-forward window, by trial index.
    /// </summary>
    Task WriteScores(OptimizationId id, int windowIndex, IReadOnlyDictionary<int, OptimizationNeighborhoodScore> scores, CancellationToken cancellationToken);

    /// <summary>
    ///     Every stored trial of an optimization, in delivery order.
    /// </summary>
    Task<IReadOnlyList<OptimizationTrialInternal>> ReadAll(OptimizationId id, CancellationToken cancellationToken);
}