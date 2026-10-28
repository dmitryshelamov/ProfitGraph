using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.OrderWaiting;

public sealed record BotOrderWaitingStarted(
    BotId BotId,
    long EngineOrderId,
    OrderWaitingCode Code,
    DateTimeOffset? Until,
    decimal? Low,
    decimal? High,
    DateTimeOffset At) : IDomainEvent;