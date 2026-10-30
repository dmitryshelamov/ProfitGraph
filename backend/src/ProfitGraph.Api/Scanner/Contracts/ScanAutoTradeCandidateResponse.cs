namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A kind of card in a backtest of the preset's conditions (ADR 0071, section 5): its trades and mean R per third among the cards
///     that pass the minimums shown, and whether they prove a rule.
/// </summary>
public sealed record ScanAutoTradeCandidateResponse(
    string Timeframe,
    string Scenario,
    string Direction,
    IReadOnlyList<ScanAutoTradeThirdResponse> Thirds,
    bool Qualifies);