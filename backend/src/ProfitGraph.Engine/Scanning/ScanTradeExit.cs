namespace ProfitGraph.Engine.Scanning;

public enum ScanTradeExit
{
    Target,
    Stop,

    /// <summary>
    ///     Neither stop nor target within 24 hours of the entry: closed at that minute's close.
    /// </summary>
    Time
}