using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.Execution;

/// <summary>
///     Keeps the planned trial total the runner reports once the candidates are filtered.
/// </summary>
internal sealed class OptimizationPlannedTrials : IProgress<OptimizationProgressInternal>
{
    private int _total = -1;

    public int? Total => Volatile.Read(ref _total) is var total and >= 0 ? total : null;

    public void Report(OptimizationProgressInternal value)
    {
        Volatile.Write(ref _total, value.Total);
    }
}