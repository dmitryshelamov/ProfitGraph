using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     A submit is held in the core for a reason (ADR 0024); raised when the wait starts or its reason changes.
/// </summary>
public sealed record OrderIntentDeferred(BotId BotId, OrderIntentId IntentId, long EngineOrderId, OrderWaitingCode Code, DateTimeOffset? Until, DateTimeOffset At)
    : IDomainEvent;