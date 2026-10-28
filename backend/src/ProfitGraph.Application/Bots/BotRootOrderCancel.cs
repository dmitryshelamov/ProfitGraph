using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     How a root resolved an owner's order cancel: the engine asked to cancel the order, or the reason it could not.
/// </summary>
public sealed record BotRootOrderCancel(OrderCancelRequestId RequestId, string? RejectionReason);