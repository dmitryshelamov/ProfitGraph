using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

public sealed record BotPositionClosed(BotId BotId, BotPositionCycleId CycleId, DateTimeOffset At, decimal RealizedPnl) : IDomainEvent;