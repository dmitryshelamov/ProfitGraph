namespace ProfitGraph.Api.Bots.Contracts;

public sealed record BotPositionCycleResponse(Guid Id, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, string? RealizedPnl, string Fees);