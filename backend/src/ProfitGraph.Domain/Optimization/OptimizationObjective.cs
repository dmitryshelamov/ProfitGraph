namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     What ranks trials; every objective reads in-sample metrics only, so the holdout never influences selection.
/// </summary>
public enum OptimizationObjective
{
    InSampleReturnPercent,
    InSampleTotalPnl,
    InSampleReturnToDrawdown
}