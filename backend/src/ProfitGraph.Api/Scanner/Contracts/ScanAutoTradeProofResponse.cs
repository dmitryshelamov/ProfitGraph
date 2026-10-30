namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A backtest of a rule's proof (ADR 0071, section 5): its id, the rule's cards per third and the reference pair's move in percent.
/// </summary>
public sealed record ScanAutoTradeProofResponse(Guid BacktestId, IReadOnlyList<ScanAutoTradeThirdResponse> Thirds, string? MarketChange);