using ProfitGraph.Application.Backtesting.Converters;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Converters;

public static class OptimizationRunInternalConverter
{
    public static OptimizationRunInternal ToInternal(this OptimizationRun optimization, OptimizationStateInternal state)
    {
        OptimizationConfiguration configuration = optimization.Configuration;
        OptimizationCounters counters = optimization.Counters;

        return new OptimizationRunInternal(
            optimization.Id.Value,
            optimization.StrategyId.Value,
            optimization.StrategyVersion.Value,
            optimization.CreatedAt,
            new OptimizationConfigurationInternal(
                configuration.Backtest.ToInternal(),
                configuration.Ranges.ToDictionary(pair => pair.Key, pair => new OptimizationRangeInternal(pair.Value.Min, pair.Value.Max, pair.Value.Step)),
                OptimizationNames.Method(configuration.Method),
                OptimizationNames.Objective(configuration.Objective),
                configuration.Budget,
                configuration.Seed,
                configuration.FinalistCount,
                configuration.Constraints.MinExecutions,
                configuration.Constraints.MaxDrawdownPercent),
            OptimizationNames.Stage(optimization.Stage),
            optimization.History?.ToInternal(),
            optimization.PlannedTrials,
            new OptimizationCountersInternal(counters.Completed, counters.RejectedByConstraint, counters.RejectedByTemplate, counters.Failed),
            optimization.Best is { } best ? new OptimizationBestTrialInternal(best.TrialIndex, best.Objective) : null,
            state);
    }
}