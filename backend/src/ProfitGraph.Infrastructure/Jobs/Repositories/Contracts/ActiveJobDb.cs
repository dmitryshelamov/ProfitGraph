namespace ProfitGraph.Infrastructure.Jobs.Repositories.Contracts;

internal sealed class ActiveJobDb
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = null!;
    public string Lane { get; init; } = null!;
    public string Status { get; init; } = null!;
    public long Completed { get; init; }
    public long? Total { get; init; }
    public bool CancelRequested { get; init; }
    public bool PauseRequested { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public long? QueuePosition { get; init; }
    public Guid? StrategyId { get; init; }
    public string? StrategyName { get; init; }
    public int? StrategyVersion { get; init; }
    public string? Venue { get; init; }
    public string? Symbol { get; init; }
    public string? Timeframe { get; init; }
    public string? Method { get; init; }
    public Guid? OptimizationId { get; init; }
}