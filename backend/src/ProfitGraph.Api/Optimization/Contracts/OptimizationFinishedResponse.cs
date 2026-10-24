namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationFinishedResponse(Guid OptimizationId, string Status);