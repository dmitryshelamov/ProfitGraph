using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot's values followed the parameters of the strategy version it now runs (ADR 0047): <see cref="Added" /> are new
///     parameters with the defaults they took, <see cref="Removed" /> the names whose values were dropped.
/// </summary>
public sealed record BotParametersFitted(BotId BotId, IReadOnlyDictionary<string, decimal> Added, IReadOnlyList<string> Removed, DateTimeOffset At)
    : IDomainEvent;