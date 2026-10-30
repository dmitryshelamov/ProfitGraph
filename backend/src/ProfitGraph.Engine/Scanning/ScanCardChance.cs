namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A card's odds from the backtest's cards like it (ADR 0071, section 1): the chance that its target comes before its stop, the
///     share of such cards whose entry filled, the mean R of those that missed the target, how many closed trades the chance rests on,
///     and the result expected after costs: <c>fill × (chance × the card's R at its target + (1 − chance) × the missed mean R)</c>.
/// </summary>
public sealed record ScanCardChance(decimal Chance, decimal FillShare, decimal MissedR, decimal ExpectedR, int Trades)
{
    /// <summary>
    ///     The odds of <paramref name="idea" /> from the <paramref name="odds" /> of its timeframe, scenario, direction and bands with at
    ///     least <paramref name="minimumTrades" /> closed trades, its R at its target with <paramref name="costs" />; null without such
    ///     odds or without the pair's NATR.
    /// </summary>
    public static ScanCardChance? Of(
        IReadOnlyList<ScanCardOdds> odds,
        string timeframe,
        ScanTradeIdea idea,
        decimal? natrPercent,
        ScanTradeCosts costs,
        int minimumTrades)
    {
        ArgumentNullException.ThrowIfNull(odds);
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(costs);

        if (ScanCardBands.Stop(idea, natrPercent) is not { } stopBand)
        {
            return null;
        }

        int chanceBand = ScanCardBands.Chance(idea.RewardRisk);
        ScanCardOdds? like = odds.FirstOrDefault(item => item.Timeframe == timeframe && item.Scenario == idea.Scenario && item.Direction == idea.Direction &&
            item.ChanceBand == chanceBand && item.StopBand == stopBand);

        if (like is null || like.Filled < minimumTrades || like.Chance() is not { } chance || like.FillShare() is not { } fill)
        {
            return null;
        }

        decimal missed = like.MissedMeanR ?? -1m;
        decimal win = ScanCardFollow.ResultR(idea, idea.Target, ScanTradeExit.Target, costs);

        return new ScanCardChance(chance, fill, missed, fill * (chance * win + (1m - chance) * missed), like.Filled);
    }
}