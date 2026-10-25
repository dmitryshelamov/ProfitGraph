using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

public sealed record BotPositionOpened(BotId BotId, BotPositionCycleId CycleId, DateTimeOffset At) : IDomainEvent;