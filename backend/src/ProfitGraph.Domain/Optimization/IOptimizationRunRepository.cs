namespace ProfitGraph.Domain.Optimization;

public interface IOptimizationRunRepository
{
    Task Add(OptimizationRun optimization, CancellationToken cancellationToken);

    Task<OptimizationRun?> Get(OptimizationId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores the stage, history, counters and best trial of an existing optimization; its configuration never changes.
    /// </summary>
    Task Save(OptimizationRun optimization, CancellationToken cancellationToken);
}