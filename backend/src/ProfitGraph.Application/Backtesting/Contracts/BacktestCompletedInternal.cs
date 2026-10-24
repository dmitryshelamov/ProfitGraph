using ProfitGraph.Application.Jobs;

namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     The job result of a completed run; the run itself holds the facts and metrics.
/// </summary>
public sealed record BacktestCompletedInternal(Guid RunId, int ProcessedCandles, decimal? TotalPnl) : IBackgroundJobResult;