namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationRangeRequest(string? Min, string? Max, string? Step);