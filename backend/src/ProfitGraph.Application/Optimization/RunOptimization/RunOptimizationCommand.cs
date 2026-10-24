using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Optimization.RunOptimization;

/// <summary>
///     Executes a queued optimization in the Worker's optimization lane; the job has the optimization's identifier.
/// </summary>
public sealed record RunOptimizationCommand(Guid OptimizationId) : IBackgroundJobRequest<OptimizationCompletedInternal>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("optimization");
}