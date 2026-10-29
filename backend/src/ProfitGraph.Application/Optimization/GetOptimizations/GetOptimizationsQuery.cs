using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.GetOptimizations;

/// <summary>
///     The user's optimizations of every strategy that pass <paramref name="Filter" />, newest first, after the optimization
///     <paramref name="After" />.
/// </summary>
public sealed record GetOptimizationsQuery(Guid UserId, OptimizationListFilterInternal Filter, Guid? After, int Limit) : IQuery<OptimizationSummaryPageInternal>;