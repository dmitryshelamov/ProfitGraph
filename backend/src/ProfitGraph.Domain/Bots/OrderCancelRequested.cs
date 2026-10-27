using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot's engine took the owner's cancel of one of its orders and asked the venue to cancel it (ADR 0048).
/// </summary>
public sealed record OrderCancelRequested(BotId BotId, OrderCancelRequestId RequestId, long EngineOrderId, DateTimeOffset At) : IDomainEvent;