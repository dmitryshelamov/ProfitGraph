namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     The prepared-history columns, with the captured rules they reference, that runs and optimizations store alike.
/// </summary>
internal interface IBacktestHistoryDb
{
    string? HistoryIdentity { get; }
    DateTimeOffset? HistoryFrom { get; }
    DateTimeOffset? ReplayFrom { get; }
    DateTimeOffset? ReplayTo { get; }
    int? WarmupCount { get; }
    int? ReplayCount { get; }
    int WarmupClosedIntervals { get; }
    int ReplayClosedIntervals { get; }
    Guid? RulesSnapshotId { get; }
    string? BaseAsset { get; }
    string? QuoteAsset { get; }
    decimal? QuantityStep { get; }
    decimal? PriceTick { get; }
    decimal? MinQuantity { get; }
    decimal? MinNotional { get; }
    int? BasePrecision { get; }
    int? QuotePrecision { get; }
    string? BuyFeeAsset { get; }
    bool? ExactQuoteFees { get; }
}