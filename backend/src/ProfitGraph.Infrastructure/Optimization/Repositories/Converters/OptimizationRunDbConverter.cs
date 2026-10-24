using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Converters;

internal static class OptimizationRunDbConverter
{
    public static OptimizationRun ToDomain(this OptimizationRunDb row)
    {
        return OptimizationRun.Restore(
            new OptimizationId(row.Id),
            new UserId(row.OwnerId),
            new StrategyId(row.StrategyId),
            new StrategyVersionNumber(row.StrategyVersion),
            row.CreatedAt.ToUniversalTime(),
            BacktestJsonSerializer.Deserialize<OptimizationConfigurationDb>(row.Configuration).ToDomain(),
            row.Stage switch
            {
                null => null,
                "loading_history" => OptimizationStage.LoadingHistory,
                "searching" => OptimizationStage.Searching,
                "starting_finalists" => OptimizationStage.StartingFinalists,
                _ => throw new InvalidOperationException($"Unknown stored optimization stage '{row.Stage}'.")
            },
            row.ToHistory(),
            new OptimizationCounters(row.Completed, row.RejectedByConstraint, row.RejectedByTemplate, row.Failed),
            row.BestTrialIndex is { } index ? new OptimizationBestTrial(index, row.BestObjective!.Value) : null,
            row.PlannedTrials);
    }

    public static string? ToDb(this OptimizationStage? stage)
    {
        return stage switch
        {
            null => null,
            OptimizationStage.LoadingHistory => "loading_history",
            OptimizationStage.Searching => "searching",
            _ => "starting_finalists"
        };
    }
}