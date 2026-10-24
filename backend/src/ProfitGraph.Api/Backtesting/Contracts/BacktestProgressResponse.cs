namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     The <c>BacktestProgress</c> hub message: one committed observation of a run, in sequence order.
/// </summary>
public sealed record BacktestProgressResponse(Guid RunId, BacktestObservationResponse Observation);