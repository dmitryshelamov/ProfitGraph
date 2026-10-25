using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotFillInternal(
    long Sequence,
    Guid Id,
    long EngineOrderId,
    OrderSide Side,
    string? Tag,
    decimal GrossBase,
    decimal GrossQuote,
    decimal Fee,
    FeeAsset FeeAsset,
    decimal Price,
    DateTimeOffset FilledAt,
    bool CommissionPending);