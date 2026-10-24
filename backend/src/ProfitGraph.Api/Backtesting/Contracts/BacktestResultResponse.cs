namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestResultResponse(
    string Outcome,
    int ProcessedCandles,
    string? Equity,
    string? TotalPnl,
    string? FailureCode,
    string? FailureMessage,
    BacktestMetricsResponse? Metrics);