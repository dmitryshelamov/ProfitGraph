namespace ProfitGraph.Application.Optimization;

/// <summary>
///     Where a listed optimization stands: still active (queued, running or paused), completed, failed or cancelled.
/// </summary>
public enum OptimizationListState
{
    Active,
    Completed,
    Failed,
    Cancelled
}