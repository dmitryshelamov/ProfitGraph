namespace ProfitGraph.Engine.Accounting;

public sealed record ExternalFeeReceiptCheckpoint(
    long OrderId,
    OrderSide Side,
    decimal GrossBase,
    decimal GrossQuote,
    decimal Fee,
    decimal RemainingShares,
    decimal PendingFeeReserve = 0m,
    OrderType Type = OrderType.Limit,
    IReadOnlyList<ExternalFeeSliceCheckpoint>? Slices = null);