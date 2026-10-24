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
    ///     Every stored trial of an optimization, in delivery order.
    /// </summary>
    Task<IReadOnlyList<OptimizationTrialInternal>> ReadAll(OptimizationId id, CancellationToken cancellationToken);
}