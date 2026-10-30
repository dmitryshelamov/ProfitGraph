namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     Where an auto trade request stands (ADR 0071, section 6): waiting for the Worker, claimed while the Worker reads the pair's cards,
///     turned into a trade or a paper trade, or skipped for a <see cref="ScanAutoTradeSkip" />.
/// </summary>
public enum ScanAutoTradeRequestStatus
{
    Pending,
    Handling,
    Created,
    Paper,
    Skipped
}