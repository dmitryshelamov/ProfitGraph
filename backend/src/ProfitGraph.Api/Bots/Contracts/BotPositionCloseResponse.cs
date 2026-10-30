using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     An owner's position close (ADR 0064): pending until the bot's runner takes it, then cancelling the bot's orders, selling the
///     position, and closed or failed with a reason code.
/// </summary>
public sealed record BotPositionCloseResponse(
    Guid Id,
    Guid BotId,
    bool StopAfter,
    BotPositionCloseStatus Status,
    int? CancelledOrders,
    long? EngineOrderId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);