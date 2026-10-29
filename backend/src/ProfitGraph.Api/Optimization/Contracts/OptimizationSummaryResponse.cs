namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationSummaryResponse(
    Guid Id,
    Guid StrategyId,
    string StrategyName,
    int StrategyVersion,
    DateTimeOffset CreatedAt,
    string Venue,
    string Symbol,
    string Timeframe,
    string Method,
    string Status,
    int ExecutedTrials,
    int? PlannedTrials,
    int? WindowCount,
    string? BestObjective);