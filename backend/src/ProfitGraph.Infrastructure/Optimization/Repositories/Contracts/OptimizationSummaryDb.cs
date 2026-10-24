namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

internal sealed class OptimizationSummaryDb
{
    public Guid Id { get; init; }
    public int StrategyVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Venue { get; init; } = null!;
    public string Symbol { get; init; } = null!;
    public string Timeframe { get; init; } = null!;
    public string Method { get; init; } = null!;
    public string Status { get; init; } = null!;
    public int ExecutedTrials { get; init; }
    public decimal? BestObjective { get; init; }
}