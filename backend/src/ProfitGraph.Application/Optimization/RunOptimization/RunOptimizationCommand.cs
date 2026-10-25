using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Optimization.RunOptimization;

/// <summary>
///     Executes a queued optimization in the Worker's optimization lane; the job has the optimization's identifier and the
///     owner's credential for market data, if any, which its finalists keep (ADR 0023, TC6).
/// </summary>
public sealed record RunOptimizationCommand(Guid OptimizationId, Guid? CredentialId = null) : IBackgroundJobRequest<OptimizationCompletedInternal>
{
    public static BackgroundJobKind Kind { get; } = new BackgroundJobKind("optimization");
}