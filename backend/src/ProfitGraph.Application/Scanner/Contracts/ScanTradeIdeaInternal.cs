using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A trade a card proposes (ADR 0060) from the levels of <see cref="Timeframe" />; <see cref="Amount" /> and <see cref="Risk" /> are
///     the owner's sizing, null without settings, and <see cref="Taken" /> says a live trade of the owner has the same scenario and prices.
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
    bool Taken);