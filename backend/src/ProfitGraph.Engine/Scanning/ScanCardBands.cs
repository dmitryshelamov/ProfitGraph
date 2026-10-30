namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The bands that make cards alike (ADR 0071, section 1): the chance a random walk gives a target <c>RR</c> risks away,
///     <c>1 / (1 + RR)</c>, in five bands, and the stop's distance from the entry in hourly NATRs in six. A card's odds are those of the
///     backtest's cards of its scenario, direction and timeframe in the same two bands. A value on an edge belongs to the lower band.
/// </summary>
public static class ScanCardBands
{
    private const decimal Percent = 100m;

    /// <summary>
    ///     The upper edges of the chance bands but the last.
    /// </summary>
    private static readonly decimal[] ChanceEdges = [0.2m, 0.3m, 0.4m, 0.5m];

    /// <summary>
    ///     The upper edges of the stop bands but the last, in hourly NATRs.
    /// </summary>
    private static readonly decimal[] StopEdges = [0.25m, 0.5m, 1m, 1.5m, 3m];

    /// <summary>
    ///     The chance band of a card with <paramref name="rewardRisk" />, from 0 (the least likely targets) up.
    /// </summary>
    public static int Chance(decimal rewardRisk)
    {
        return Band(1m / (1m + Math.Max(rewardRisk, val2: 0m)), ChanceEdges);
    }

    /// <summary>
    ///     The stop band of a card, from 0 (the nearest stops) up; null without the pair's NATR.
    /// </summary>
    public static int? Stop(ScanTradeIdea idea, decimal? natrPercent)
    {
        ArgumentNullException.ThrowIfNull(idea);

        if (natrPercent is not > 0m || idea.Entry <= 0m)
        {
            return null;
        }

        return Band(Math.Abs(idea.Entry - idea.Stop) / idea.Entry * Percent / natrPercent.Value, StopEdges);
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