namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

internal sealed class OptimizationRangeDb
{
    public decimal Min { get; init; }
    public decimal Max { get; init; }
    public decimal Step { get; init; }
}