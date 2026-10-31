namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The bands that make cards alike (ADR 0071, section 1): the chance a random walk gives a target <c>RR</c> risks away,
///     <c>1 / (1 + RR)</c>, in five bands, the stop's distance from the entry in hourly NATRs in six, and whether the venue's reference
///     pair was above its 50-hour EMA (ADR 0073). A card's odds are those of the backtests' cards of its scenario, direction and timeframe
///     in the same bands. A value on an edge belongs to the lower band.
/// </summary>
public static class ScanCardBands
{
    private const decimal Percent = 100m;

    /// <summary>
    ///     The upper edges of the chance bands but the last.
    /// </summary>
    private static readonly decimal[] ChanceEdges = [0.2m, 0.3m, 0.4m, 0.5m];

    /// <summary>
    ///     The middle of each chance band, the geometry of odds stored before ADR 0073 without their own.
    /// </summary>
    private static readonly decimal[] ChanceCenters = [0.15m, 0.25m, 0.35m, 0.45m, 0.6m];

    /// <summary>
    ///     The upper edges of the stop bands but the last, in hourly NATRs.
    /// </summary>
    private static readonly decimal[] StopEdges = [0.25m, 0.5m, 1m, 1.5m, 3m];

    /// <summary>
    ///     The chance a random walk gives a target <paramref name="rewardRisk" /> risks away before the stop: <c>1 / (1 + RR)</c>.
    /// </summary>
    public static decimal Geometry(decimal rewardRisk)
    {
        return 1m / (1m + Math.Max(rewardRisk, val2: 0m));
    }

    /// <summary>
    ///     The chance band of a card with <paramref name="rewardRisk" />, from 0 (the least likely targets) up.
    /// </summary>
    public static int Chance(decimal rewardRisk)
    {
        return Band(Geometry(rewardRisk), ChanceEdges);
    }

    /// <summary>
    ///     The middle of the chance band <paramref name="band" />.
    /// </summary>
    public static decimal ChanceCenter(int band)
    {
        return ChanceCenters[Math.Clamp(band, min: 0, ChanceCenters.Length - 1)];
    }

    /// <summary>
    ///     The stop band of a card, from 0 (the nearest stops) up; null without the pair's NATR.
    /// </summary>
    public static int? Stop(ScanTradeIdea idea, decimal? natrPercent)
    {
        return StopNatr(idea, natrPercent) is { } distance ? Stop(distance) : null;
    }

    /// <summary>
    ///     The stop band of a stop <paramref name="stopNatr" /> hourly NATRs from the entry.
    /// </summary>
    public static int Stop(decimal stopNatr)
    {
        return Band(stopNatr, StopEdges);
    }

    /// <summary>
    ///     The stop's distance from the entry in hourly NATRs; null without the pair's NATR.
    /// </summary>
    public static decimal? StopNatr(ScanTradeIdea idea, decimal? natrPercent)
    {
        ArgumentNullException.ThrowIfNull(idea);

        if (natrPercent is not > 0m || idea.Entry <= 0m)
        {
            return null;
        }

        return Math.Abs(idea.Entry - idea.Stop) / idea.Entry * Percent / natrPercent.Value;
    }

    /// <summary>
    ///     The market band of the reference pair's <see cref="ScanMetrics.MarketTrend" />: 1 above its 50-hour EMA, 0 at or below it, null
    ///     without one.
    /// </summary>
    public static int? Market(decimal? marketTrend)
    {
        return marketTrend switch
        {
            null => null,
            > 0m => 1,
            _ => 0
        };
    }

    private static int Band(decimal value, decimal[] edges)
    {
        var band = 0;

        while (band < edges.Length && value > edges[band])
        {
            band++;
        }

        return band;
    }
}