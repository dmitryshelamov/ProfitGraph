namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A trade scenario over every signal: closed trades, the share with a positive result (0 to 1), mean, median and total R, the mean
///     result in percent, the longest losing run and the mean minutes in a trade.
/// </summary>
public sealed record ScanScenarioSummaryResponse(
    string Scenario,
    int Trades,
    string? WinningShare,
    string? MeanR,
    string? MedianR,
    string TotalR,
    string? MeanPercent,
    int LongestLosingStreak,
    string? MeanMinutes,
    string Direction);