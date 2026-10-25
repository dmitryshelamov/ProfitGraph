namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotPositionCycleInternal(Guid Id, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, decimal? RealizedPnl);