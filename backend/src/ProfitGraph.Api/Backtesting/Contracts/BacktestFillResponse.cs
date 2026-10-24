namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestFillResponse(
    long OrderId,
    string? Tag,
    string Side,
    string Type,
    string Price,
    string GrossBase,
    string BaseChange,
    string QuoteChange,
    string Fee,
    string FeeAsset,
    string RealizedPnl);