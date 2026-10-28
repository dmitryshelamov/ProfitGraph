using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     A submit was rejected before or by the venue; the engine receives the rejection.
/// </summary>
public sealed record OrderIntentRejected(BotId BotId, OrderIntentId IntentId, long EngineOrderId, RejectionReason Reason, DateTimeOffset At) : IDomainEvent;