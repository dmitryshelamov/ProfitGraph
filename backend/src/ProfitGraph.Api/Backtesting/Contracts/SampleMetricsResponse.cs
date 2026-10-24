namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record SampleMetricsResponse(
    int StartIndex,
    int EndExclusive,
    int ObservedCandles,
    string? BoundaryEquity,
    string? EndEquity,
    string? TotalPnl,
    string? RealizedPnl,
    string? UnrealizedPnl,
    string? ReturnPercent,
    string? FeesBase,
    string? FeesQuote,
    string? MaxDrawdownPercent,
    long Executions);