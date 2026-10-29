namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A trade card's proposal (ADR 0060): the timeframe whose levels gave it, the scenario, its prices, reward-to-risk and how many
///     minutes its entry waits; with the owner's settings, the quote it spends and risks; and whether a live trade already took it.
///     Decimals are exact strings.
/// </summary>
public sealed record ScanTradeIdeaResponse(
    string Timeframe,
    string Scenario,
    string Entry,
    string Stop,
    string Target,
    string RewardRisk,
    int EntryValidityMinutes,
    string? Amount,
    string? Risk,
    bool Taken);