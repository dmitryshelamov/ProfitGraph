namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     A <c>backtest_runs</c> row with the captured rules it references.
/// </summary>
internal sealed class BacktestRunDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public Guid StrategyId { get; init; }
    public int StrategyVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Configuration { get; init; } = null!;
    public string? Stage { get; init; }
    public string? HistoryIdentity { get; init; }
    public DateTimeOffset? HistoryFrom { get; init; }
    public DateTimeOffset? ReplayFrom { get; init; }
    public DateTimeOffset? ReplayTo { get; init; }
    public int? WarmupCount { get; init; }
    public int? ReplayCount { get; init; }
    public Guid? RulesSnapshotId { get; init; }
    public string? BaseAsset { get; init; }
    public string? QuoteAsset { get; init; }
    public decimal? QuantityStep { get; init; }
    public decimal? PriceTick { get; init; }
    public decimal? MinQuantity { get; init; }
    public decimal? MinNotional { get; init; }
    public int? BasePrecision { get; init; }
    public int? QuotePrecision { get; init; }
    public string? BuyFeeAsset { get; init; }
    public bool? ExactQuoteFees { get; init; }
    public string? Outcome { get; init; }
    public int? ProcessedCandles { get; init; }
    public decimal? Equity { get; init; }
    public decimal? TotalPnl { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
    public string? Metrics { get; init; }
}