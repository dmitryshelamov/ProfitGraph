using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Trend and momentum of a pair's closed hours (ADR 0071, section 3): exponential moving averages of the last 20 and 50 hourly
///     closes, each seeded by the simple mean of its first closes of the window, and Wilder's RSI of 14 hourly closes, the engine's
///     indicator (ADR 0056). The scanner's metrics and the replay call the same function on the same up to 168 hours, so both see the
///     same values. A value without enough hours is empty. Deterministic.
/// </summary>
public static class ScanTrend
{
    public const int FastPeriod = 20;
    public const int SlowPeriod = 50;
    public const int RsiPeriod = 14;
    private const decimal Percent = 100m;

    /// <summary>
    ///     <see cref="ScanMetrics.TrendEma50" />, <see cref="ScanMetrics.EmaSpread20x50" /> and <see cref="ScanMetrics.Rsi1h" /> of the
    ///     hours at <paramref name="price" />.
    /// </summary>
    public static ScanMetrics Apply(ScanMetrics metrics, IReadOnlyList<ScanCandle> hours, decimal? price)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(hours);

        (decimal? fast, decimal? slow, decimal? rsi) = Of(hours);

        return With(metrics, fast, slow, rsi, price);
    }

    /// <summary>
    ///     The fast and slow averages and the RSI of the hours.
    /// </summary>
    public static (decimal? Fast, decimal? Slow, decimal? Rsi) Of(IReadOnlyList<ScanCandle> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);

        var strength = new WilderRelativeStrength(RsiPeriod);
        decimal? rsi = null;

        foreach (ScanCandle hour in hours)
        {
            rsi = strength.Add(hour.Close);
        }

        return (Ema(hours, FastPeriod), Ema(hours, SlowPeriod), rsi);
    }

    /// <summary>
    ///     The metrics with the averages and RSI at <paramref name="price" />: the price against the slow average and the fast against the
    ///     slow, in percent.
    /// </summary>
    public static ScanMetrics With(ScanMetrics metrics, decimal? fast, decimal? slow, decimal? rsi, decimal? price)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return metrics with
        {
            TrendEma50 = slow is > 0m && price is { } current ? (current - slow.Value) / slow.Value * Percent : null,
            EmaSpread20x50 = slow is > 0m && fast is { } quick ? (quick - slow.Value) / slow.Value * Percent : null,
            Rsi1h = rsi
        };
    }

    private static decimal? Ema(IReadOnlyList<ScanCandle> hours, int period)
    {
        if (hours.Count < period)
        {
            return null;
        }

        decimal sum = 0;

        for (var i = 0; i < period; i++)
        {
            sum += hours[i].Close;
        }

        decimal average = sum / period;
        decimal weight = 2m / (period + 1);

        for (int i = period; i < hours.Count; i++)
        {
            average += weight * (hours[i].Close - average);
        }

        return average;
    }
}