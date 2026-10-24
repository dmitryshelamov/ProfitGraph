namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     The answer to a subscription: the run's last stored observation, to read earlier ones over HTTP, and its outcome
///     once it ended.
/// </summary>
public sealed record BacktestCursorResponse(long LastSequence, string? Outcome);