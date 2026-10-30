namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     Why an auto trade request made no trade (ADR 0071, section 6).
/// </summary>
public enum ScanAutoTradeSkip
{
    /// <summary>
    ///     Its snapshot was older than the request lifetime when it was handled.
    /// </summary>
    Expired,

    /// <summary>
    ///     The rule was off or paused for the day.
    /// </summary>
    Paused,

    /// <summary>
    ///     The rule had its most trades open.
    /// </summary>
    LimitOpen,

    /// <summary>
    ///     The rule had created its most trades of the UTC day.
    /// </summary>
    LimitDay,

    /// <summary>
    ///     The pair already had a live trade of the rule.
    /// </summary>
    PairBusy,

    /// <summary>
    ///     The pair had no card of the rule's kind, or the card's odds were missing or below the rule's minimums.
    /// </summary>
    NoIdea,

    /// <summary>
    ///     Creating the trade was refused; the request keeps the error code.
    /// </summary>
    Refused
}