using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The owner stopped the bot; its open orders stay as they are.
/// </summary>
public sealed record BotStopped(BotId BotId, DateTimeOffset At) : IDomainEvent;