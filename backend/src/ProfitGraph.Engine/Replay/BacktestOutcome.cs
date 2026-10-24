namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Terminal state of one pure replay invocation, including inspectable partial results.
/// </summary>
public enum BacktestOutcome
{
    Completed,
    Cancelled,
    Failed
}