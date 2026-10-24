using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Ports;

/// <summary>
///     Stores trial batches in the caller's transaction; a trial already stored under its sequence is left unchanged.
/// </summary>
public interface IOptimizationTrialStore
{
    Task Write(OptimizationId id, IReadOnlyList<OptimizationTrialInternal> trials, CancellationToken cancellationToken);
}