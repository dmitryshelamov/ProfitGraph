using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The runtime stopped processing the bot.
/// </summary>
public sealed record BotRuntimeStopped(BotId BotId, DateTimeOffset At) : IDomainEvent;