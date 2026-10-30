using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A preset's auto trade rule (ADR 0071, section 4): its card and minimums, limits, mode, the UTC day it is paused for and the
///     backtests that proved it, and since when it is in its mode.
/// </summary>
public sealed record ScanAutoTradeRuleInternal(
    Guid Id,
    Guid PresetId,
    string Venue,
    PositionDirection Direction,
    ScanScenario Scenario,
    string Timeframe,
    decimal MinExpectedR,
    decimal? MinChance,
    int MaxOpenTrades,
    int MaxTradesPerDay,
    decimal DailyLossLimit,
    ScanAutoTradeMode Mode,
    DateOnly? PausedOn,
    ScanAutoTradeProof? Proof,
    DateTimeOffset ModeSince,
    DateTimeOffset UpdatedAt);