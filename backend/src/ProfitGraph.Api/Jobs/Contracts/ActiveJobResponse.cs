namespace ProfitGraph.Api.Jobs.Contracts;

/// <summary>
///     A queued, running or paused job of the user. <c>Kind</c> is <c>backtest</c>, <c>optimization</c> or
///     <c>historical_download</c>; <c>Lane</c> is the Worker lane (<c>main</c> or <c>optimization</c>), which runs one job
///     at a time; <c>QueuePosition</c> is a queued job's place among every user's queued jobs of the lane, 1 being next.
/// </summary>
public sealed record ActiveJobResponse(
    Guid Id,
    string Kind,
    string Lane,
    string Status,
    long Completed,
    long? Total,
    bool CancelRequested,
    bool PauseRequested,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    int? QueuePosition,
    Guid? StrategyId,
    string? StrategyName,
    int? StrategyVersion,
    string? Venue,
    string? Symbol,
    string? Timeframe,
    string? Method,
    Guid? OptimizationId);