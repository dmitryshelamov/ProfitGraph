using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.OrderWaiting;

public sealed record BotOrderWaitingEnded(BotId BotId, long EngineOrderId, OrderWaitingCode Code, DateTimeOffset At) : IDomainEvent;