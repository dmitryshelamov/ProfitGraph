using ProfitGraph.Domain.Bots.OrderCancels;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record OrderCancelInternal(
    Guid Id,
    Guid BotId,
    long EngineOrderId,
    OrderCancelStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);