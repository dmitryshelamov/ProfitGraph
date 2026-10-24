namespace ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

internal sealed class StrategyVersionDb
{
    public Guid Id { get; init; }
    public Guid StrategyId { get; init; }
    public int Number { get; init; }
    public string SnapshotJson { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}