using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Application.Jobs.Contracts;

/// <summary>
///     A queued, running or paused job of the user with what it works on. The Worker runs one job at a time in each
///     <paramref name="Lane" />; <paramref name="QueuePosition" /> is the queued job's place among every user's queued jobs
///     of that lane, 1 being next. The strategy belongs to a backtest or an optimization; <paramref name="Method" /> to an
///     optimization; <paramref name="OptimizationId" /> to a backtest an optimization started.
/// </summary>
public sealed record ActiveJobInternal(
    Guid Id,
    string Kind,
    string Lane,
    BackgroundJobStatus Status,
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