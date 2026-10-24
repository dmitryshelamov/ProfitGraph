using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Optimization.Ports;

/// <summary>
///     Bounded reads of optimizations and their trials. Callers check ownership first with <see cref="Owner" />.
/// </summary>
public interface IOptimizationReader
{
    Task<UserId?> Owner(OptimizationId id, CancellationToken cancellationToken);

    Task<OptimizationStateInternal> State(OptimizationId id, CancellationToken cancellationToken);

    Task<OptimizationTrialPageInternal> Trials(OptimizationId id, long afterSequence, int limit, CancellationToken cancellationToken);

    Task<OptimizationSummaryPageInternal> ForStrategy(StrategyId strategyId, Guid? after, int limit, CancellationToken cancellationToken);
}