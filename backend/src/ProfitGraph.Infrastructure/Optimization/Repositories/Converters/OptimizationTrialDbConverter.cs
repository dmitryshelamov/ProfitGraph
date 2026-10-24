using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Converters;

internal static class OptimizationTrialDbConverter
{
    public static OptimizationTrialInternal ToInternal(this OptimizationTrialDb row)
    {
        return new OptimizationTrialInternal(
            row.Sequence,
            row.TrialIndex,
            BacktestJsonSerializer.Deserialize<Dictionary<string, decimal>>(row.Parameters),
            row.Status,
            row.FailureCode,
            row.Objective,
            row.ReturnPercent,
            row.TotalPnl,
            row.MaxDrawdownPercent,
            row.Executions,
            row.ElapsedMs,
            row.Stage,
            row.NeighborsEvaluated is { } evaluated ? new OptimizationNeighborhoodInternal(evaluated, row.NeighborsEligible!.Value, row.NeighborhoodMean) : null);
    }
}