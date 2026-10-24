using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Application.Optimization.Execution;

/// <summary>
///     Turns a stored trial back into its result for the candidate the search enumerates at the same index, so a resumed
///     search ranks and selects finalists exactly as an uninterrupted one (Profit Fox Designer ADR 0044).
/// </summary>
internal static class OptimizationTrialRestoring
{
    /// <param name="trial">The candidate the search enumerates at the stored trial's index.</param>
    /// <param name="stored">The trial an earlier attempt stored.</param>
    /// <param name="inSampleBars">The in-sample candles, which the restored metrics cover.</param>
    /// <param name="rejectedByTemplate">Whether the template rules reject the candidate now; the stored trial must agree.</param>
    public static OptimizationTrialResult Restore(OptimizationTrial trial, OptimizationTrialInternal stored, int inSampleBars, bool rejectedByTemplate)
    {
        OptimizationTrialStatus status = OptimizationNames.ParseTrialStatus(stored.Status);
        IReadOnlyDictionary<PortReference, decimal> selections = trial.Selections.OptimizedValues;

        if (stored.TrialIndex != trial.Index || stored.Stage != trial.Stage || status == OptimizationTrialStatus.RejectedByTemplate != rejectedByTemplate ||
            stored.Parameters.Count != selections.Count ||
            selections.Any(pair => !stored.Parameters.TryGetValue(pair.Key.NodeId, out decimal value) || value != pair.Value))
        {
            throw new OptimizationException(OptimizationErrorCodes.PersistedTrialMismatch, "A stored trial does not match the frozen search candidate at its index.");
        }

        SampleMetrics? metrics = status is OptimizationTrialStatus.Completed or OptimizationTrialStatus.RejectedByConstraint
            ? new SampleMetrics(
                StartIndex: 0,
                inSampleBars,
                inSampleBars,
                BoundaryEquity: null,
                EndEquity: null,
                stored.TotalPnl,
                RealizedPnl: null,
                UnrealizedPnl: null,
                stored.ReturnPercent,
                FeesBase: null,
                FeesQuote: null,
                stored.MaxDrawdownPercent,
                stored.Executions)
            : null;

        return OptimizationTrialResult.Restore(trial, metrics, status, stored.Objective, stored.FailureCode, stored.ElapsedMs);
    }
}