namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     One pair's scanner metrics as exact decimal strings; percentages are in percent, an empty metric is null.
/// </summary>
public sealed record ScanSnapshotRowResponse(
    string Symbol,
    string? Price,
    string? Change5m,
    string? Change15m,
    string? Change1h,
    string? Change4h,
    string? Change24h,
    string? Turnover1h,
    string? Turnover24h,
    string? VolumeSpike,
    string? VolumeSpike5m,
    string? VolumeSpike15m,
    string? Natr24h,
    string? EfficiencyRatio24h,
    string? DistanceToHigh24h,
    string? DistanceToLow24h,
    string? DistanceToHigh7d,
    string? DistanceToLow7d,
    string? Breakout4h,
    string? Range4h,
    string? Resistance,
    string? Support,
    string? DistanceToResistance,
    string? DistanceToSupport,
    string? RewardRisk,
    string? Spread,
    int? ListingAgeDays,
    bool Stale,
    bool Incomplete);