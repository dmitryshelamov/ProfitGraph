using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotPositionCloseInternal(
    Guid Id,
    Guid BotId,
    bool StopAfter,
    BotPositionCloseStatus Status,
    int? CancelledOrders,
    long? EngineOrderId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);