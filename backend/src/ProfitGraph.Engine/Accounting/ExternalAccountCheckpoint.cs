using System.Collections.Immutable;

namespace ProfitGraph.Engine.Accounting;

public sealed record ExternalAccountCheckpoint(
    decimal StartingCapital,
    decimal QuoteTotal,
    decimal QuoteReserved,
    decimal BaseTotal,
    decimal BaseReserved,
    decimal CostBasis,
    decimal RealizedPnl,
    decimal FeesBase,
    decimal FeesQuote,
    decimal? LastBuyPrice,
    long NextOrderId,
    ImmutableArray<OrderSnapshot> Orders,
    ImmutableArray<ExternalOrderProgressCheckpoint> Progress,
    ImmutableArray<ExternalFeeReceiptCheckpoint> FeeReceipts = default)
{
    public ImmutableArray<ExternalFeeReceiptCheckpoint> FeeReceipts { get; init; } = FeeReceipts.IsDefault ? [] : FeeReceipts;
}