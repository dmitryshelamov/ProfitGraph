using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A closed trade of a card a preset backtest followed (ADR 0071, section 5): its kind, its signal's time and third of the period,
///     its result in R after costs, and the chance and expected R its kind and bands gave it in the same backtest with their cautious ends
///     (ADR 0073), empty when they had too few trades. Auto trade rules are proven by the cards whose cautious values their minimums
///     accept.
/// </summary>
public sealed record ScanBacktestCard(
    string Timeframe,
    ScanScenario Scenario,
    PositionDirection Direction,
    DateTimeOffset SignalAt,
    int Third,
    decimal ResultR,
    decimal? Chance,
    decimal? ExpectedR,
    decimal? ChanceLow = null,
    decimal? ExpectedRLow = null);