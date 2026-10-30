namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A preset's auto trades (ADR 0071, sections 4–7): the rule or null, today's open, created and lost, the minimums the backtests'
///     cards are shown at, the latest backtest of the preset's conditions and the latest one of the opposite market, the latest
///     requests and paper trades, and the closed paper trades' count and mean R since the rule's mode began. Decimals are exact strings.
/// </summary>
public sealed record ScanAutoTradeResponse(
    Guid PresetId,
    string Venue,
    ScanAutoTradeRuleResponse? Rule,
    int OpenTrades,
    int TodayTrades,
    string TodayLoss,
    string MinExpectedR,
    string? MinChance,
    ScanAutoTradeBacktestResponse? Latest,
    ScanAutoTradeBacktestResponse? OtherRegime,
    IReadOnlyList<ScanAutoTradeRequestResponse> Requests,
    IReadOnlyList<ScanPaperTradeResponse> PaperTrades,
    int PaperClosed,
    string? PaperMeanR);