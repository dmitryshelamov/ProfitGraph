namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Continuous-account segment contribution, normalized by run capital; unobserved boundaries and nonrepresentable derived values are explicit.
///     The capital cost metrics (ADR 0057) are null for a run stored before them.
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
    long Executions,
    decimal? MaxCapitalDeployed = null,
    decimal? MaxCapitalDeployedPercent = null,
    decimal? MaxCapitalCommitted = null,
    decimal? TimeInPositionPercent = null,
    decimal? TimeUnderWaterPercent = null,
    int? LongestUnderWaterCandles = null);