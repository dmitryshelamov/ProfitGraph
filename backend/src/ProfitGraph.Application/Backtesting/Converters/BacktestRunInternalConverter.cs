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
            new BacktestConfigurationInternal(
                configuration.Series.Venue.Value,
                configuration.Series.Symbol.Value,
                configuration.Series.Timeframe.Key,
                configuration.From,
                configuration.To,
                configuration.ObservedAt,
                configuration.Parameters,
                configuration.Settings.StartingCapital,
                configuration.Settings.Slippage,
                configuration.Settings.LimitFeeRate,
                configuration.Settings.MarketFeeRate,
                configuration.Split.Fraction,
                configuration.WarmupCandles),
            run.Stage switch
            {
                null => null,
                BacktestRunStage.LoadingHistory => "loading_history",
                _ => "replaying"
            },
            history is null
                ? null
                : new BacktestHistoryInternal(
                    history.Identity,
                    history.HistoryFrom,
                    history.From,
                    history.To,
                    history.WarmupCount,
                    history.ReplayCount,
                    history.RulesSnapshotId.Value,
                    history.BaseAsset,
                    history.QuoteAsset,
                    history.Rules),
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