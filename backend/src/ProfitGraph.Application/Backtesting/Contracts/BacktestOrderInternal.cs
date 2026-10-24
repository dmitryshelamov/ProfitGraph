namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestOrderInternal(
    long Id,
    string? Tag,
    string Side,
    string Type,
    decimal? Quantity,
    decimal? LimitPrice,
    decimal? QuoteBudget,
    decimal Reservation,
    string Status,
    string OriginNodeId);