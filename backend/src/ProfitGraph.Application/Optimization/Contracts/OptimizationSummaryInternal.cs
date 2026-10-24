namespace ProfitGraph.Application.Optimization.Contracts;

public sealed record OptimizationSummaryInternal(
    Guid Id,
    int StrategyVersion,
    DateTimeOffset CreatedAt,
    string Venue,
    string Symbol,
    string Timeframe,
    string Method,
    string Status,
    int ExecutedTrials,
    decimal? BestObjective);