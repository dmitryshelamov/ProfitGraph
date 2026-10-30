namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     How a rule's cards did in one completed preset backtest (ADR 0071, section 5): the backtest, the trades and mean R of each third
///     of its period, and how far the venue's reference pair moved over the period, in percent, empty when it is unknown.
/// </summary>
public sealed record ScanAutoTradeEvidence(ScanBacktestId BacktestId, IReadOnlyList<ScanAutoTradeThird> Thirds, decimal? MarketChange)
{
    public int Trades => Thirds.Sum(third => third.Trades);

    /// <summary>
    ///     Whether the cards had enough closed trades and a mean R of at least the minimum in each third.
    /// </summary>
    public bool Proves(ScanAutoTradeThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        return Thirds.Count == 3 && Trades >= thresholds.MinBacktestTrades && Thirds.All(third => third.MeanR >= thresholds.MinBacktestMeanR);
    }
}