using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Ports;

/// <summary>
///     Signals, after the Worker commits, that an optimization stored trials, changed its stage or finished (stage 4 plan,
///     S4-12). A signal carries no trials and may name another kind of job: readers take what they need from storage.
/// </summary>
public interface IOptimizationProgressNotifications
{
    IAsyncEnumerable<OptimizationId> Listen(CancellationToken cancellationToken);
}