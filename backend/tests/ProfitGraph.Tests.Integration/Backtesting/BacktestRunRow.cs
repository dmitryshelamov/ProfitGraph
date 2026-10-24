namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     The stored state of a run that the run tests check.
/// </summary>
internal sealed record BacktestRunRow(
    string? Stage,
    string? Outcome,
    int? ProcessedCandles,
    int? ReplayCount,
    decimal? TotalPnl,
    long CandleCount,
    long LedgerCount,
    bool HasMetrics);