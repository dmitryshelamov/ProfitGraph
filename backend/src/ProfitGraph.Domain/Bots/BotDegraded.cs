using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot entered a degraded state or its degradation reason changed. <see cref="Detail" /> is a short diagnostic
///     text that the runtime considered safe to show to the owner (ADR 0029), or null.
/// </summary>
public sealed record BotDegraded(BotId BotId, BotStatusReason Reason, DateTimeOffset At, string? Detail = null) : IDomainEvent;