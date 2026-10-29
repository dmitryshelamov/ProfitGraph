namespace ProfitGraph.Domain.Scanner.Trades;

/// <summary>
///     Where a scanner trade is (ADR 0060). <see cref="Waiting" /> and <see cref="Open" /> are live; every other status is final.
/// </summary>
public enum ScanTradeStatus
{
    /// <summary>
    ///     The bot runs and its entry waits for a fill.
    /// </summary>
    Waiting,

    /// <summary>
    ///     The entry bought; the target and the stop guard the position.
    /// </summary>
    Open,

    /// <summary>
    ///     The target sold the position.
    /// </summary>
    TookProfit,

    /// <summary>
    ///     The stop sold the position.
    /// </summary>
    StoppedOut,

    /// <summary>
    ///     The position closed otherwise, such as by the owner's own sale.
    /// </summary>
    Closed,

    /// <summary>
    ///     The entry was not filled in time and was cancelled.
    /// </summary>
    Expired,

    /// <summary>
    ///     The bot stopped before its entry bought anything.
    /// </summary>
    Cancelled,

    /// <summary>
    ///     The bot could not start or the venue rejected the entry.
    /// </summary>
    Failed
}