using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     How a replay ended: its outcome, the candles it processed, the final valuation when it is representable, the
///     failure that stopped it and the metrics of the observed candles.
/// </summary>
public sealed record BacktestRunResult(
    BacktestOutcome Outcome,
    int ProcessedCandles,
    decimal? Equity,
    decimal? TotalPnl,
    string? FailureCode,
    string? FailureMessage,
    BacktestRunMetrics? Metrics);