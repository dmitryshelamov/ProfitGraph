namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestOrderResponse(
    long Id,
    string? Tag,
    string Side,
    string Type,
    string? Quantity,
    string? LimitPrice,
    string? QuoteBudget,
    string Reservation,
    string Status,
    string OriginNodeId);