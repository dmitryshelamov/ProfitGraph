using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.GetOptimizationTrials;

/// <summary>
///     Stored trials after a delivery sequence, in delivery order.
/// </summary>
public sealed record GetOptimizationTrialsQuery(Guid UserId, Guid Id, long After, int Limit) : IQuery<OptimizationTrialPageInternal>
{
    public const int MaxLimit = 500;
}