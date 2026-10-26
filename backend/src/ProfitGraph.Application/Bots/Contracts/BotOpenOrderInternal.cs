using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotOpenOrderInternal(
    long EngineOrderId,
    string? Tag,
    OrderSide Side,
    OrderType Type,
    OrderStatus Status,
    decimal? Quantity,
    decimal? LimitPrice,
    decimal? QuoteBudget,
    decimal FilledBase);
