namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationTrialPageResponse(IReadOnlyList<OptimizationTrialResponse> Items, long? Next);