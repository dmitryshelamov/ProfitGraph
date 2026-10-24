namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationSummaryPageResponse(IReadOnlyList<OptimizationSummaryResponse> Items, Guid? Next);