namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     One sample's metrics; the capital cost metrics (ADR 0057) are null for a run stored before them.
/// </summary>
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
    long Executions,
    string? MaxCapitalDeployed,
    string? MaxCapitalDeployedPercent,
    string? MaxCapitalCommitted,
    string? TimeInPositionPercent,
    string? TimeUnderWaterPercent,
    int? LongestUnderWaterCandles);