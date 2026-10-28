using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     Another bot of the same key that should run, with its capital as an exact string in its symbol's quote currency.
/// </summary>
public sealed record BotAllocationResponse(Guid BotId, string? Name, string Symbol, string AllocationCap, BotRuntimeStatus RuntimeStatus);