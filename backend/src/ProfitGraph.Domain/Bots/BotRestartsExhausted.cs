using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A runtime failure spent the bot's last automatic restart; it waits for its owner (ADR 0016, ADR 0042).
/// </summary>
public sealed record BotRestartsExhausted(BotId BotId, DateTimeOffset At) : IDomainEvent;