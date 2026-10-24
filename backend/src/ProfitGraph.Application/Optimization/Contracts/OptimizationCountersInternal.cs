namespace ProfitGraph.Application.Optimization.Contracts;

public sealed record OptimizationCountersInternal(int Completed, int RejectedByConstraint, int RejectedByTemplate, int Failed);