using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     An intent failed every delivery attempt; the bot degrades.
/// </summary>
public sealed record OrderIntentFailed(BotId BotId, OrderIntentId IntentId, long EngineOrderId, DateTimeOffset At) : IDomainEvent;