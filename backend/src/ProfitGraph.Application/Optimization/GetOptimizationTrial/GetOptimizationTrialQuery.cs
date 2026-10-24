using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.GetOptimizationTrial;

/// <summary>
///     One stored trial, by its index in the search and, for walk-forward, its window.
/// </summary>
public sealed record GetOptimizationTrialQuery(Guid UserId, Guid Id, int WindowIndex, int TrialIndex) : IQuery<OptimizationTrialInternal>;