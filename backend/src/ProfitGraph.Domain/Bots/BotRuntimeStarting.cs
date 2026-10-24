using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A runtime took the bot's lease and is opening its engine.
/// </summary>
public sealed record BotRuntimeStarting(BotId BotId, DateTimeOffset At) : IDomainEvent;