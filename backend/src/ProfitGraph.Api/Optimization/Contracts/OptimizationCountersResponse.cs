namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationCountersResponse(int Completed, int RejectedByConstraint, int RejectedByTemplate, int Failed);