using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The capital another bot of the same credential is allowed, in its symbol's quote currency (ADR 0049).
/// </summary>
public sealed record BotAllocationInternal(Guid BotId, string? Name, string Symbol, decimal AllocationCap, BotRuntimeStatus RuntimeStatus);