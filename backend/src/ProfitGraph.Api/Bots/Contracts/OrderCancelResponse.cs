using ProfitGraph.Domain.Bots.OrderCancels;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     An owner's cancel of one of the bot's orders (ADR 0048): <c>Pending</c> until the bot's runner takes it, then
///     <c>Requested</c> when the engine asked the venue to cancel the order, or <c>Rejected</c> with a stable reason.
/// </summary>
public sealed record OrderCancelResponse(
    Guid Id,
    Guid BotId,
    long EngineOrderId,
    OrderCancelStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);