namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationSummaryResponse(
    Guid Id,
    int StrategyVersion,
    DateTimeOffset CreatedAt,
    string Venue,
    string Symbol,
    string Timeframe,
    string Method,
    string Status,
    int ExecutedTrials,
    string? BestObjective);