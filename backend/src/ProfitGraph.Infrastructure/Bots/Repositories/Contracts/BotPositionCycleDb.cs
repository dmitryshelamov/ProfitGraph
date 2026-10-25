namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotPositionCycleDb
{
    public Guid Id { get; init; }
    public Guid BotId { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
    public decimal RealizedPnlAtOpen { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public decimal? RealizedPnl { get; init; }
}