using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner;

public static class ScanMetricValues
{
    /// <summary>
    ///     The value of <paramref name="metric" /> in a pair's metrics; null when it is empty.
    /// </summary>
    public static decimal? Of(this ScanMetrics metrics, ScanMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return metric switch
        {
            ScanMetric.Price => metrics.Price,
            ScanMetric.Change5m => metrics.Change5m,
            ScanMetric.Change15m => metrics.Change15m,
            ScanMetric.Change1h => metrics.Change1h,
            ScanMetric.Change4h => metrics.Change4h,
            ScanMetric.Change24h => metrics.Change24h,
            ScanMetric.Turnover1h => metrics.Turnover1h,
            ScanMetric.Turnover24h => metrics.Turnover24h,
            ScanMetric.VolumeSpike => metrics.VolumeSpike,
            ScanMetric.Natr24h => metrics.Natr24h,
            ScanMetric.EfficiencyRatio24h => metrics.EfficiencyRatio24h,
            ScanMetric.DistanceToHigh24h => metrics.DistanceToHigh24h,
            ScanMetric.DistanceToLow24h => metrics.DistanceToLow24h,
            ScanMetric.DistanceToHigh7d => metrics.DistanceToHigh7d,
            ScanMetric.DistanceToLow7d => metrics.DistanceToLow7d,
            ScanMetric.Resistance => metrics.Resistance,
            ScanMetric.Support => metrics.Support,
            ScanMetric.DistanceToResistance => metrics.DistanceToResistance,
            ScanMetric.DistanceToSupport => metrics.DistanceToSupport,
            ScanMetric.RewardRisk => metrics.RewardRisk,
            ScanMetric.Spread => metrics.Spread,
            ScanMetric.ListingAgeDays => metrics.ListingAgeDays,
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown scanner metric.")
        };
    }
}