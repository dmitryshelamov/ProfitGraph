using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot entered a degraded state or its degradation reason changed.
/// </summary>
public sealed record BotDegraded(BotId BotId, BotStatusReason Reason, DateTimeOffset At) : IDomainEvent;