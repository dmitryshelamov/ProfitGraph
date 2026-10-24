using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The owner changed the bot's risk limits.
/// </summary>
public sealed record BotRiskLimitsChanged(BotId BotId, DateTimeOffset At) : IDomainEvent;