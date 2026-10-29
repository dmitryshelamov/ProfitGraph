namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Support and resistance from closed candles of one timeframe (ADR 0059, section 8): swing highs and lows (above or below the two
///     candles before and not below or above the two after) and the extremes of the candles, prices within 0.5 % merged into their
///     mean. The scanner's metrics use its hours; a pair's chart uses the candles of its timeframe. The nearest resistance lies more
///     than 0.1 % above the price, the nearest support more than 0.1 % below it. Deterministic.
/// </summary>
public static class ScanLevels
{
    /// <summary>
    ///     The fewest closed candles levels are drawn from.
    /// </summary>
    public const int MinimumCandles = 24;

    /// <summary>
    ///     The most closed candles levels are drawn from: the scanner's week of hours.
    /// </summary>
    public const int MaximumCandles = 168;

    private const int SwingSide = 2;
    private const decimal MergeWithin = 0.005m;
    private const decimal Clearance = 0.001m;
    private const decimal Percent = 100m;

    /// <summary>
    ///     The levels of the candles, lowest first; none below <see cref="MinimumCandles" />.
    /// </summary>
    public static IReadOnlyList<decimal> Of(IReadOnlyList<ScanCandle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (candles.Count < MinimumCandles)
        {
            return [];
        }

        List<decimal> prices = [candles.Max(candle => candle.High), candles.Min(candle => candle.Low)];

        for (int i = SwingSide; i < candles.Count - SwingSide; i++)
        {
            decimal high = candles[i].High;
            decimal low = candles[i].Low;

            if (high > candles[i - 1].High && high > candles[i - 2].High && high >= candles[i + 1].High && high >= candles[i + 2].High)
            {
                prices.Add(high);
            }

            if (low < candles[i - 1].Low && low < candles[i - 2].Low && low <= candles[i + 1].Low && low <= candles[i + 2].Low)
            {
                prices.Add(low);
            }
        }

        prices.Sort();
        List<decimal> levels = [];
        decimal clusterStart = prices[0];
        decimal clusterSum = 0;
        var clusterCount = 0;

        foreach (decimal price in prices)
        {
            if (price > clusterStart * (1 + MergeWithin))
            {
                levels.Add(clusterSum / clusterCount);
                clusterStart = price;
                clusterSum = 0;
                clusterCount = 0;
            }

            clusterSum += price;
            clusterCount++;
        }

        levels.Add(clusterSum / clusterCount);

        return levels;
    }

    /// <summary>
    ///     The level metrics at <paramref name="price" />: the nearest resistance and support, their distances in percent and the
    ///     reward-to-risk ratio of a long position, each empty without the levels it needs.
    /// </summary>
    public static ScanMetrics Apply(ScanMetrics metrics, IReadOnlyList<decimal> levels, decimal? price)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(levels);

        if (price is not { } current || levels.Count == 0)
        {
            return metrics;
        }

        decimal? resistance = null;
        decimal? support = null;

        foreach (decimal level in levels)
        {
            if (level < current * (1 - Clearance))
            {
                support = level;
            }
            else if (level > current * (1 + Clearance))
            {
                resistance = level;

                break;
            }
        }

        decimal? toResistance = resistance is { } r ? (r - current) / current * Percent : null;
        decimal? toSupport = support is { } s ? (s - current) / current * Percent : null;

        return metrics with
        {
            Resistance = resistance,
            Support = support,
            DistanceToResistance = toResistance,
            DistanceToSupport = toSupport,
            RewardRisk = toResistance is { } up && toSupport is { } down ? up / -down : null
        };
    }
}