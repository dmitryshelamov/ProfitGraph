using ProfitGraph.Domain.Bots.OrderWaiting;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A working order its venue holds locally; bounds are exact invariant decimal strings.
/// </summary>
public sealed record BotOrderWaitingResponse(long EngineOrderId, OrderWaitingCode Code, DateTimeOffset Since, DateTimeOffset? Until, string? Low, string? High);