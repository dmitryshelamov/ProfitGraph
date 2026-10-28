using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     A submit may have left the process for the venue for the first time.
/// </summary>
public sealed record OrderIntentPlaceStarted(BotId BotId, OrderIntentId IntentId, long EngineOrderId, DateTimeOffset At) : IDomainEvent;