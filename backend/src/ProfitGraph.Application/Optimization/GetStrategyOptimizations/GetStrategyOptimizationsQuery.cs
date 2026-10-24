using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.GetStrategyOptimizations;

/// <summary>
///     Optimizations of one of the user's strategies, newest first, after the optimization <paramref name="After" />.
/// </summary>
public sealed record GetStrategyOptimizationsQuery(Guid UserId, Guid StrategyId, Guid? After, int Limit) : IQuery<OptimizationSummaryPageInternal>
{
    public const int MaxLimit = 100;
}