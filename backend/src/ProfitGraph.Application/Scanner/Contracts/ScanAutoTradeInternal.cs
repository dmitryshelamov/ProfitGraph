using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A preset's auto trades on its page (ADR 0071, sections 4–7): the rule, null before one is saved, today's tally, the backtests of
///     the preset's conditions with their kinds of cards at the minimums shown, the latest requests and paper trades, and the closed
///     paper trades' count and mean R next to the backtest's.
/// </summary>
public sealed record ScanAutoTradeInternal(
    Guid PresetId,
    string Venue,
    ScanAutoTradeRuleInternal? Rule,
    ScanAutoTradeTally Today,
    decimal MinExpectedR,
    decimal? MinChance,
    ScanAutoTradeEvidenceInternal Evidence,
    IReadOnlyList<ScanAutoTradeRequestInternal> Requests,
    IReadOnlyList<ScanPaperTradeInternal> PaperTrades,
    int PaperClosed,
    decimal? PaperMeanR);