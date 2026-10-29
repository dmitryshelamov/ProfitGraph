namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A trade card's proposal (ADR 0060): the scenario, its prices, reward-to-risk and how many minutes its entry waits, and, with the
///     owner's settings, the quote it spends and risks; decimals are exact strings.
/// </summary>
public sealed record ScanTradeIdeaResponse(
    string Scenario,
    string Entry,
    string Stop,
    string Target,
    string RewardRisk,
    int EntryValidityMinutes,
    string? Amount,
    string? Risk);