using ProfitGraph.Domain.Bots.ManualOrders;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     How a root resolved a manual order: the engine order it became, or the engine's rejection reason.
/// </summary>
public sealed record BotRootManualOrder(ManualOrderRequestId RequestId, long? EngineOrderId, string? RejectionReason);