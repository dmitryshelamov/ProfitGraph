namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     A cycle of the bot's position with the fees of its fills in quote.
/// </summary>
public sealed record BotPositionCycleInternal(Guid Id, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, decimal? RealizedPnl, decimal Fees);