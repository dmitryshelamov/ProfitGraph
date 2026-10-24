namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     The <c>BacktestFinished</c> hub message: the run stored its result; its job records the final status right after.
/// </summary>
public sealed record BacktestFinishedResponse(Guid RunId, string Outcome);