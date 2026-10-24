using ProfitGraph.Domain.Optimization;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Converters;

internal static class OptimizationConfigurationDbConverter
{
    public static OptimizationConfigurationDb ToDb(this OptimizationConfiguration configuration)
    {
        return new OptimizationConfigurationDb
        {
            Backtest = configuration.Backtest.ToDb(),
            Ranges = configuration.Ranges.ToDictionary(
                pair => pair.Key,
                pair => new OptimizationRangeDb { Min = pair.Value.Min, Max = pair.Value.Max, Step = pair.Value.Step },
                StringComparer.Ordinal),
            Method = configuration.Method,
            Objective = configuration.Objective,
            Budget = configuration.Budget,
            Seed = configuration.Seed,
            FinalistCount = configuration.FinalistCount,
            MinExecutions = configuration.Constraints.MinExecutions,
            MaxDrawdownPercent = configuration.Constraints.MaxDrawdownPercent
        };
    }

    public static OptimizationConfiguration ToDomain(this OptimizationConfigurationDb row)
    {
        return new OptimizationConfiguration(
            row.Backtest.ToDomain(),
            row.Ranges.ToDictionary(pair => pair.Key, pair => new DecimalOptimizeParamValue(pair.Value.Min, pair.Value.Max, pair.Value.Step), StringComparer.Ordinal),
            row.Method,
            row.Objective,
            new OptimizationConstraints(row.MinExecutions, row.MaxDrawdownPercent),
            row.Budget,
            row.Seed,
            row.FinalistCount);
    }
}