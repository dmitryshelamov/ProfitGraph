using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record ManualOrderInternal(
    Guid Id,
    Guid BotId,
    OrderSide Side,
    decimal Amount,
    ManualOrderStatus Status,
    long? EngineOrderId,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);