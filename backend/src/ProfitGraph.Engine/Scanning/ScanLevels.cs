namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Support and resistance from closed hours (ADR 0059, section 8): swing highs and lows (above or below the two hours before and
///     not below or above the two after) and the extremes of the hours, prices within 0.5 % merged into their mean. The nearest
///     resistance lies more than 0.1 % above the price, the nearest support more than 0.1 % below it. Deterministic.
/// </summary>
public static class ScanLevels
{
    /// <summary>
    ///     The fewest closed hours levels are drawn from.
    /// </summary>
    public const int MinimumHours = 24;

    private const int SwingSide = 2;
    private const decimal MergeWithin = 0.005m;
    private const decimal Clearance = 0.001m;
    private const decimal Percent = 100m;

    /// <summary>
    ///     The levels of the hours, lowest first; none below <see cref="MinimumHours" />.
    /// </summary>
    public static IReadOnlyList<decimal> Of(IReadOnlyList<ScanCandle> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);

        if (hours.Count < MinimumHours)
        {
            return [];
        }

        List<decimal> prices = [hours.Max(hour => hour.High), hours.Min(hour => hour.Low)];

        for (int i = SwingSide; i < hours.Count - SwingSide; i++)
        {
            decimal high = hours[i].High;
            decimal low = hours[i].Low;

            if (high > hours[i - 1].High && high > hours[i - 2].High && high >= hours[i + 1].High && high >= hours[i + 2].High)
            {
                prices.Add(high);
            }

            if (low < hours[i - 1].Low && low < hours[i - 2].Low && low <= hours[i + 1].Low && low <= hours[i + 2].Low)
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
