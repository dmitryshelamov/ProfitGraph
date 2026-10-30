using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A kind of card in a backtest of the preset's conditions (ADR 0071, section 5): its closed trades and mean R in each third of the
///     period among the cards that pass the minimums, and whether they prove a rule there.
/// </summary>
public sealed record ScanAutoTradeCandidateInternal(
    string Timeframe,
    ScanScenario Scenario,
    PositionDirection Direction,
    IReadOnlyList<ScanAutoTradeThird> Thirds,
    bool Qualifies);