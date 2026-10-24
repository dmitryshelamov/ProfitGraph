namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Полное исполнение ордера. GrossBase — валовое количество; BaseChange и QuoteChange — фактические изменения балансов
///     со знаком; Fee удержана в FeeAsset. RealizedPnl ненулевой только для продажи.
/// </summary>
public sealed record Fill(
    long OrderId,
    string? Tag,
    OrderSide Side,
    OrderType Type,
    decimal Price,
    decimal GrossBase,
    decimal BaseChange,
    decimal QuoteChange,
    decimal Fee,
    FeeAsset FeeAsset,
    decimal RealizedPnl);