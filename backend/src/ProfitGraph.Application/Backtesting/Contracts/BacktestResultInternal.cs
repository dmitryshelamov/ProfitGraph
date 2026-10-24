namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestResultInternal(
    string Outcome,
    int ProcessedCandles,
    decimal? Equity,
    decimal? TotalPnl,
    string? FailureCode,
    string? FailureMessage,
    BacktestMetricsInternal? Metrics);