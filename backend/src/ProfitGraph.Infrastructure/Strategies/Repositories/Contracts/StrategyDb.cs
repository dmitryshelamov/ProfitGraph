namespace ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

internal sealed class StrategyDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int CurrentVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}