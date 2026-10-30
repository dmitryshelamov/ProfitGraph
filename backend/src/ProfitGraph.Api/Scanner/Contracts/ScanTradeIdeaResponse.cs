namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A trade card's proposal (ADR 0060): the timeframe whose levels gave it, the scenario, its prices, reward-to-risk and how many
///     minutes its entry waits; with the owner's settings, the quote it spends, risks and makes at its target after fees (ADR 0065); whether a live
///     trade already took it, its direction (ADR 0061), and its odds from the latest preset backtest (ADR 0071, section 1), null
///     without enough cards like it.
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
    bool Taken,
    string Direction,
    string? Profit,
    ScanCardChanceResponse? Odds);