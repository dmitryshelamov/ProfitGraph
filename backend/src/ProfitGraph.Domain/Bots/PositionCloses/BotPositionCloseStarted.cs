using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.PositionCloses;

public sealed record BotPositionCloseStarted(BotId BotId, BotPositionCloseId CloseId, int CancelledOrders, bool StopAfter, DateTimeOffset At) : IDomainEvent;