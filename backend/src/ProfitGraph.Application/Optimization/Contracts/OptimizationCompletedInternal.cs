using ProfitGraph.Application.Jobs;

namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     The job result of a completed optimization; the optimization holds its trials and finalists.
/// </summary>
public sealed record OptimizationCompletedInternal(Guid OptimizationId, int ExecutedTrials, int Finalists) : IBackgroundJobResult;