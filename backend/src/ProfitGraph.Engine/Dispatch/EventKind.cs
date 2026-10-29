namespace ProfitGraph.Engine.Dispatch;

public enum EventKind
{
    Start,
    CandleClosed,
    Submitted,
    Filled,
    Cancelled,
    Rejected,
    CancelCompleted,
    Resume,
    GridStarted,
    GridStopped
}