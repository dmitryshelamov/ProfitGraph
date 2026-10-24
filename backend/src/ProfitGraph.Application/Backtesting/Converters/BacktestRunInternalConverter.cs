using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Converters;

public static class BacktestRunInternalConverter
{
    public static BacktestRunInternal ToInternal(this BacktestRun run, BacktestRunStateInternal state)
    {
        BacktestConfiguration configuration = run.Configuration;
        BacktestHistoryInfo? history = run.History;
        BacktestRunResult? result = run.Result;

        return new BacktestRunInternal(
            run.Id.Value,
            run.StrategyId.Value,
            run.StrategyVersion.Value,
            run.CreatedAt,
            configuration.ToInternal(),
            run.Stage switch
            {
                null => null,
                BacktestRunStage.LoadingHistory => "loading_history",
                _ => "replaying"
            },
            history?.ToInternal(),
            result is null
                ? null
                : new BacktestResultInternal(
                    result.Outcome switch
                    {
                        BacktestOutcome.Completed => "completed",
                        BacktestOutcome.Cancelled => "cancelled",
                        _ => "failed"
                    },
                    result.ProcessedCandles,
                    result.Equity,
                    result.TotalPnl,
                    result.FailureCode,
                    result.FailureMessage,
                    result.Metrics is { } metrics ? new BacktestMetricsInternal(metrics.Full, metrics.InSample, metrics.OutOfSample) : null),
            state);
    }
}