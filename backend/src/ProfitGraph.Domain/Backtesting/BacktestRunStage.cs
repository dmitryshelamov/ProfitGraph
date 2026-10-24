namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     How far execution of a run got; the background job alone owns whether the run is queued, running or finished.
/// </summary>
public enum BacktestRunStage
{
    LoadingHistory,
    Replaying
}