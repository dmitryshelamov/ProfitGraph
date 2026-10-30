using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     How the backtest's cards of one kind ended (ADR 0071, section 1): the timeframe, scenario, direction and bands, how many cards
///     were proposed, how many filled and closed, how many of those reached their target first and how many their stop, the mean R of
///     every closed trade and of those that did not reach their target (the stopped ones and those held to the horizon).
/// </summary>
public sealed record ScanCardOdds(
    string Timeframe,
    ScanScenario Scenario,
    PositionDirection Direction,
    int ChanceBand,
    int StopBand,
    int Cards,
    int Filled,
    int Targets,
    int Stops,
    decimal? MeanR,
    decimal? MissedMeanR)
{
    /// <summary>
    ///     The share of closed trades that reached their target first; null without one.
    /// </summary>
    public decimal? Chance()
    {
        return Filled == 0 ? null : (decimal)Targets / Filled;
    }

    /// <summary>
    ///     The share of proposed cards whose entry filled.
    /// </summary>
    public decimal? FillShare()
    {
        return Cards == 0 ? null : (decimal)Filled / Cards;
    }
}