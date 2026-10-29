namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     A run in a list: what it ran on, its job status, the optimization that started it, if any, and, once it completed,
///     its result.
/// </summary>
public sealed record BacktestSummaryInternal(
    Guid Id,
    Guid StrategyId,
    string StrategyName,
    int StrategyVersion,
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset CreatedAt,
    string Status,
    string? Outcome,
    string? QuoteAsset,
    decimal? TotalPnl,
    decimal? ReturnPercent,
    decimal? MaxDrawdownPercent,
    Guid? OptimizationId);