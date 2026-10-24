namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

internal sealed class OptimizationCursorDb
{
    public long LastSequence { get; init; }
    public string Status { get; init; } = null!;
}