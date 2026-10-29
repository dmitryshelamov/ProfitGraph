using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A scenario over every signal: how many became closed trades, the share with a positive result, mean, median and total R, the mean
///     result in percent, the longest run of losing trades in time order and the mean minutes in a trade.
/// </summary>
public sealed record ScanScenarioSummary(
    ScanScenario Scenario,
    int Trades,
    decimal? WinningShare,
    decimal? MeanR,
    decimal? MedianR,
    decimal TotalR,
    decimal? MeanPercent,
    int LongestLosingStreak,
    decimal? MeanMinutes,
    PositionDirection Direction = PositionDirection.Long);