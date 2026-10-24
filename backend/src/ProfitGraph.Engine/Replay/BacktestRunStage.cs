namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Execution preparation stage; background job status alone owns run lifecycle.
/// </summary>
public enum BacktestRunStage
{
    LoadingHistory,
    Replaying
}