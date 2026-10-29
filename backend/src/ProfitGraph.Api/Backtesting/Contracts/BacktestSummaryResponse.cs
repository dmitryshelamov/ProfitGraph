namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestSummaryResponse(
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
    string? TotalPnl,
    string? ReturnPercent,
    string? MaxDrawdownPercent,
    Guid? OptimizationId);