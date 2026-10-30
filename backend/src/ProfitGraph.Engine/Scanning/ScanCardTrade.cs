using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A card the preset backtest proposed at a signal and followed by the bot's rules (ADR 0071, section 2): the pair, the signal's
///     time, the timeframe whose levels gave it, its scenario and direction, its bands (<see cref="ScanCardBands" />; no stop band
///     without the pair's NATR), its R at its target with the pair's costs and how it ended.
/// </summary>
public sealed record ScanCardTrade(
    string Symbol,
    DateTimeOffset SignalAt,
    string Timeframe,
    ScanScenario Scenario,
    PositionDirection Direction,
    int ChanceBand,
    int? StopBand,
    decimal TargetR,
    ScanCardOutcome Outcome);