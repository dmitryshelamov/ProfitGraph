using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Api.Bots.Contracts;

public sealed record ManualOrderResponse(
    Guid Id,
    Guid BotId,
    OrderSide Side,
    string Amount,
    ManualOrderStatus Status,
    long? EngineOrderId,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);