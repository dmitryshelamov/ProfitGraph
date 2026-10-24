using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Ports;

/// <summary>
///     Finished walk-forward windows and the summary, written in the caller's transaction and read back on resume.
/// </summary>
public interface IOptimizationWindowStore
{
    Task WriteWindow(OptimizationId id, WalkForwardWindowResult window, CancellationToken cancellationToken);

    Task<IReadOnlyList<WalkForwardWindowResult>> ReadWindows(OptimizationId id, CancellationToken cancellationToken);

    Task WriteSummary(OptimizationId id, WalkForwardSummary summary, CancellationToken cancellationToken);

    Task<WalkForwardSummary?> ReadSummary(OptimizationId id, CancellationToken cancellationToken);
}