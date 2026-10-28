using ProfitGraph.Domain.Bots.ManualOrders;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record ManualOrderInternal(
    Guid Id,
    Guid BotId,
    OrderSide Side,
    OrderType Type,
    decimal Amount,
    decimal? LimitPrice,
    ManualOrderStatus Status,
    long? EngineOrderId,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);