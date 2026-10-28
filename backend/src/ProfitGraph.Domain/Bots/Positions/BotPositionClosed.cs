using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.Positions;

public sealed record BotPositionClosed(BotId BotId, BotPositionCycleId CycleId, DateTimeOffset At, decimal RealizedPnl) : IDomainEvent;