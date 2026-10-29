namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Метрики одной пары сканера (ADR 0059, раздел 3). Проценты — в процентах, не в долях; метрика без достаточной
///     истории пуста.
/// </summary>
public sealed record ScanMetrics
{
    public decimal? Price { get; init; }
    public decimal? Change5m { get; init; }
    public decimal? Change15m { get; init; }
    public decimal? Change1h { get; init; }
    public decimal? Change4h { get; init; }
    public decimal? Change24h { get; init; }
    public decimal? Turnover1h { get; init; }
    public decimal? Turnover24h { get; init; }
    public decimal? VolumeSpike { get; init; }
    public decimal? Natr24h { get; init; }
    public decimal? EfficiencyRatio24h { get; init; }
    public decimal? DistanceToHigh24h { get; init; }
    public decimal? DistanceToLow24h { get; init; }
    public decimal? DistanceToHigh7d { get; init; }
    public decimal? DistanceToLow7d { get; init; }
    public decimal? Spread { get; init; }
    public int? ListingAgeDays { get; init; }
    public bool Stale { get; init; }
}