namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     How far execution of an optimization got; the background job alone owns whether it is queued, running or finished.
/// </summary>
public enum OptimizationStage
{
    LoadingHistory,
    Searching,
    StartingFinalists
}