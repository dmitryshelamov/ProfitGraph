using ProfitGraph.Application.Optimization;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Converters;

internal static class OptimizationSummaryDbConverter
{
    public static OptimizationSummaryInternal ToInternal(this OptimizationSummaryDb row)
    {
        return new OptimizationSummaryInternal(
            row.Id,
            row.StrategyId,
            row.StrategyName,
            row.StrategyVersion,
            row.CreatedAt.ToUniversalTime(),
            row.Venue,
            row.Symbol,
            row.Timeframe,
            OptimizationNames.Method(Enum.Parse<OptimizationSearchMethod>(row.Method)),
            row.Status,
            row.ExecutedTrials,
            row.PlannedTrials,
            row.WindowCount,
            row.BestObjective);
    }
}