namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     An optimization in a list: what it searched, where it stands and its best objective so far.
///     <paramref name="PlannedTrials" /> is known once the search space is resolved; <paramref name="WindowCount" /> is the
///     number of walk-forward windows, or null for a single search.
/// </summary>
public sealed record OptimizationSummaryInternal(
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
    decimal? BestObjective);