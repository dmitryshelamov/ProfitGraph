namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Committed trading facts; terminal release is distinguished from strategy cancellation.
/// </summary>
public enum BacktestLedgerKind
{
    Submitted,
    Cancelled,
    Filled,
    Rejected,
    ReleasedTerminal
}