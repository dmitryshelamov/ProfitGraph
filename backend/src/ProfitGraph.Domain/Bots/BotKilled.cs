using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A kill switch or the daily loss limit stopped the bot; its open orders are to be cancelled.
/// </summary>
public sealed record BotKilled(BotId BotId, BotStatusReason Reason, DateTimeOffset At) : IDomainEvent;