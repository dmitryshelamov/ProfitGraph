namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A numeric scanner metric a preset can filter and sort by (ADR 0059, section 3).
/// </summary>
public enum ScanMetric
{
    Price,
    Change5m,
    Change15m,
    Change1h,
    Change4h,
    Change24h,
    Turnover1h,
    Turnover24h,
    VolumeSpike,
    VolumeSpike5m,
    VolumeSpike15m,
    Natr24h,
    EfficiencyRatio24h,
    DistanceToHigh24h,
    DistanceToLow24h,
    DistanceToHigh7d,
    DistanceToLow7d,
    Breakout4h,
    Breakdown4h,
    Range4h,
    Resistance,
    Support,
    DistanceToResistance,
    DistanceToSupport,
    RewardRisk,
    Spread,
    ListingAgeDays
}