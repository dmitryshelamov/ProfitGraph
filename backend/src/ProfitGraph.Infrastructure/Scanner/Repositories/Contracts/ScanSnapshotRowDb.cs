namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanSnapshotRowDb
{
    public string Venue { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public DateTimeOffset ComputedAt { get; init; }
    public decimal? Price { get; init; }
    public decimal? Change5m { get; init; }
    public decimal? Change15m { get; init; }
    public decimal? Change1h { get; init; }
    public decimal? Change4h { get; init; }
    public decimal? Change24h { get; init; }
    public decimal? Turnover1h { get; init; }
    public decimal? Turnover24h { get; init; }
    public decimal? VolumeSpike { get; init; }
    public decimal? VolumeSpike5m { get; init; }
    public decimal? VolumeSpike15m { get; init; }
    public decimal? Natr24h { get; init; }
    public decimal? EfficiencyRatio24h { get; init; }
    public decimal? DistanceToHigh24h { get; init; }
    public decimal? DistanceToLow24h { get; init; }
    public decimal? DistanceToHigh7d { get; init; }
    public decimal? DistanceToLow7d { get; init; }
    public decimal? Breakout4h { get; init; }
    public decimal? Breakdown4h { get; init; }
    public decimal? Range4h { get; init; }
    public decimal? Resistance { get; init; }
    public decimal? Support { get; init; }
    public decimal? DistanceToResistance { get; init; }
    public decimal? DistanceToSupport { get; init; }
    public decimal? RewardRisk { get; init; }
    public decimal? Spread { get; init; }
    public int? ListingAgeDays { get; init; }
    public bool Stale { get; init; }
    public bool Incomplete { get; init; }
}