using ProfitGraph.Domain.Optimization.WalkForward;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Optimization.Repositories.Converters;

internal static class OptimizationWindowDbConverter
{
    public static WalkForwardWindowResult ToDomain(this OptimizationWindowDb row)
    {
        return new WalkForwardWindowResult(
            new WalkForwardWindow(row.WindowIndex, row.TrainStart, row.TrainCount, row.TestStart, row.TestCount),
            row.TrainFrom.ToUniversalTime(),
            row.TrainTo.ToUniversalTime(),
            row.TestFrom.ToUniversalTime(),
            row.TestTo.ToUniversalTime(),
            row.WinnerTrialIndex,
            row.Parameters is null
                ? null
                : new EngineExecutionInputs(
                    BacktestJsonSerializer.Deserialize<Dictionary<string, decimal>>(row.Parameters)
                        .ToDictionary(pair => new PortReference(pair.Key, "value"), pair => pair.Value)),
            row.TrainMetrics is null ? null : BacktestJsonSerializer.Deserialize<SampleMetrics>(row.TrainMetrics),
            row.TestMetrics is null ? null : BacktestJsonSerializer.Deserialize<SampleMetrics>(row.TestMetrics),
            row.Status switch
            {
                "completed" => WalkForwardWindowStatus.Completed,
                "no_winner" => WalkForwardWindowStatus.NoWinner,
                "test_failed" => WalkForwardWindowStatus.TestFailed,
                _ => throw new InvalidOperationException($"Unknown stored window status '{row.Status}'.")
            },
            row.FailureCode,
            BacktestJsonSerializer.Deserialize<WalkForwardEquityPoint[]>(row.Equity));
    }

    public static string ToDb(this WalkForwardWindowStatus status)
    {
        return status switch
        {
            WalkForwardWindowStatus.Completed => "completed",
            WalkForwardWindowStatus.NoWinner => "no_winner",
            _ => "test_failed"
        };
    }
}