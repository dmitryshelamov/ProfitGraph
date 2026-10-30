namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A backtest of the preset's conditions a rule is judged by (ADR 0071, section 5): its period, the reference pair's move in
///     percent and its kinds of cards.
/// </summary>
public sealed record ScanAutoTradeBacktestResponse(
    Guid BacktestId,
    string PresetName,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset CompletedAt,
    string? MarketChange,
    IReadOnlyList<ScanAutoTradeCandidateResponse> Candidates);