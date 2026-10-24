namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Continuous-account segment contribution, normalized by run capital; unobserved boundaries and nonrepresentable derived values are explicit.
/// </summary>
public sealed record SampleMetrics(
    int StartIndex,
    int EndExclusive,
    int ObservedCandles,
    decimal? BoundaryEquity,
    decimal? EndEquity,
    decimal? TotalPnl,
    decimal? RealizedPnl,
    decimal? UnrealizedPnl,
    decimal? ReturnPercent,
    decimal? FeesBase,
    decimal? FeesQuote,
    decimal? MaxDrawdownPercent,
    long Executions);