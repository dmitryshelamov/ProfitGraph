namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

internal sealed class BacktestSummaryDb
{
    public Guid Id { get; init; }
    public Guid StrategyId { get; init; }
    public string StrategyName { get; init; } = null!;
    public int StrategyVersion { get; init; }
    public string Venue { get; init; } = null!;
    public string Symbol { get; init; } = null!;
    public string Timeframe { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
    public string Status { get; init; } = null!;
    public string? Outcome { get; init; }
    public string? QuoteAsset { get; init; }
    public decimal? TotalPnl { get; init; }
    public decimal? ReturnPercent { get; init; }
    public decimal? MaxDrawdownPercent { get; init; }
    public Guid? OptimizationId { get; init; }
}