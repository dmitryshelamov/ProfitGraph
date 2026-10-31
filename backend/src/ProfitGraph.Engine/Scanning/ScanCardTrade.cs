using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A card the preset backtest proposed at a signal and followed by the bot's rules (ADR 0071, section 2): the pair, the signal's
///     time, the timeframe whose levels gave it, its scenario and direction, its bands (<see cref="ScanCardBands" />; no stop band
///     without the pair's NATR), its R at its target with the pair's costs and how it ended. For its odds (ADR 0073) it also carries
///     its random walk's chance <c>1 / (1 + RR)</c>, the reference pair's market band at the signal, the stop's distance and the costs
///     of a stopped trade in hourly NATRs, and for a trade that left at its stop how far beyond the stop it left, in NATRs; each empty
///     without what it needs.
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
    ScanCardOutcome Outcome,
    decimal Geometry,
    int? MarketBand = null,
    decimal? StopNatr = null,
    decimal? CostNatr = null,
    decimal? Slip = null);