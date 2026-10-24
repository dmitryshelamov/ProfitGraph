namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationProgressResponse(Guid OptimizationId, OptimizationTrialResponse Trial);