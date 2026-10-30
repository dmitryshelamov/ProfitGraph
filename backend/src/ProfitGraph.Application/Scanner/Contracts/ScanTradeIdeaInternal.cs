using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A trade a card proposes (ADR 0060) from the levels of <see cref="Timeframe" />; <see cref="Amount" /> and <see cref="Risk" /> are
///     the owner's sizing, null without settings, <see cref="Profit" /> the quote it makes at its target after fees (ADR 0065), and
///     <see cref="Taken" /> says a live trade of the owner has the same scenario and prices; <see cref="Odds" /> are the latest preset
///     backtest's for cards like it (ADR 0071, section 1), null without enough of them.
/// </summary>
public sealed record ScanTradeIdeaInternal(
    string Timeframe,
    ScanScenario Scenario,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal RewardRisk,
    TimeSpan EntryValidity,
    decimal? Amount,
    decimal? Risk,
    bool Taken,
    PositionDirection Direction = PositionDirection.Long,
    decimal? Profit = null,
    ScanCardChance? Odds = null);