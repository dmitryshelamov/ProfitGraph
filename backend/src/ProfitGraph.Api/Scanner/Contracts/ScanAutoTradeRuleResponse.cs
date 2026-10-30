namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A preset's auto trade rule (ADR 0071, section 4): its card and minimums, limits, mode and since when, the UTC day it is paused
///     for and the two backtests that proved it. Decimals are exact strings.
/// </summary>
public sealed record ScanAutoTradeRuleResponse(
    Guid Id,
    Guid PresetId,
    string Venue,
    string Direction,
    string Scenario,
    string Timeframe,
    string MinExpectedR,
    string? MinChance,
    int MaxOpenTrades,
    int MaxTradesPerDay,
    string DailyLossLimit,
    string Mode,
    DateTimeOffset ModeSince,
    DateOnly? PausedOn,
    ScanAutoTradeProofResponse? Backtest,
    ScanAutoTradeProofResponse? OtherRegime);