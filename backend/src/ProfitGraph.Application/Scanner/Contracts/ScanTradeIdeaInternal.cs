using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A trade a card proposes (ADR 0060); <see cref="Amount" /> and <see cref="Risk" /> are the owner's sizing, null without settings.
/// </summary>
public sealed record ScanTradeIdeaInternal(
    ScanScenario Scenario,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal RewardRisk,
    TimeSpan EntryValidity,
    decimal? Amount,
    decimal? Risk);