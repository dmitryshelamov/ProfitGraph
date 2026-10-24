namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestFillInternal(
    long OrderId,
    string? Tag,
    string Side,
    string Type,
    decimal Price,
    decimal GrossBase,
    decimal BaseChange,
    decimal QuoteChange,
    decimal Fee,
    string FeeAsset,
    decimal RealizedPnl);