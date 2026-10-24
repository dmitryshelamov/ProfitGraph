using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

/// <summary>
///     An <c>optimizations</c> row with the captured rules it references.
/// </summary>
internal sealed class OptimizationRunDb : IBacktestHistoryDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public Guid StrategyId { get; init; }
    public int StrategyVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Configuration { get; init; } = null!;
    public string? Stage { get; init; }
    public int? PlannedTrials { get; init; }
    public int Completed { get; init; }
    public int RejectedByConstraint { get; init; }
    public int RejectedByTemplate { get; init; }
    public int Failed { get; init; }
    public int? BestTrialIndex { get; init; }
    public decimal? BestObjective { get; init; }
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
}