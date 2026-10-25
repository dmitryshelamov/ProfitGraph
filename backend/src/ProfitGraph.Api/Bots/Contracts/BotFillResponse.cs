using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Api.Bots.Contracts;

public sealed record BotFillResponse(
    long Sequence,
    Guid Id,
    long EngineOrderId,
    OrderSide Side,
    string? Tag,
    string GrossBase,
    string GrossQuote,
    string Fee,
    FeeAsset FeeAsset,
    string Price,
    DateTimeOffset FilledAt);