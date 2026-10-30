namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     What an auto trade rule does with its preset's signals (ADR 0071, section 4): nothing, paper trades (section 7) or trades with
///     their own bots (section 8). A rule moves only <c>Off → Paper → Live</c> and back to <c>Off</c>.
/// </summary>
public enum ScanAutoTradeMode
{
    Off,
    Paper,
    Live
}