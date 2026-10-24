namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     Where a run's progress stands when a client subscribes: its last stored observation and, once it ended, its outcome.
/// </summary>
public sealed record BacktestCursorInternal(long LastSequence, string? Outcome);