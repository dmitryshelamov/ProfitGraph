using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;

internal static class BacktestRunDbConverter
{
    public static BacktestRun ToDomain(this BacktestRunDb row)
    {
        BacktestHistoryInfo? history = row.HistoryIdentity is null
            ? null
            : new BacktestHistoryInfo(
                row.HistoryIdentity,
                row.HistoryFrom!.Value.ToUniversalTime(),
                row.ReplayFrom!.Value.ToUniversalTime(),
                row.ReplayTo!.Value.ToUniversalTime(),
                row.WarmupCount!.Value,
                row.ReplayCount!.Value,
                new InstrumentRulesSnapshotId(row.RulesSnapshotId!.Value),
                row.BaseAsset!,
                row.QuoteAsset!,
                new InstrumentRules(
                    row.QuantityStep!.Value,
                    row.PriceTick!.Value,
                    row.MinQuantity!.Value,
                    row.MinNotional!.Value,
                    row.BasePrecision!.Value,
                    row.QuotePrecision!.Value,
                    row.BuyFeeAsset == "base" ? FeeAsset.Base : FeeAsset.Quote,
                    row.ExactQuoteFees!.Value));
        BacktestRunResult? result = row.Outcome is null
            ? null
            : new BacktestRunResult(
                Outcome(row.Outcome),
                row.ProcessedCandles!.Value,
                row.Equity,
                row.TotalPnl,
                row.FailureCode,
                row.FailureMessage,
                row.Metrics is null ? null : BacktestJsonSerializer.Deserialize<BacktestRunMetrics>(row.Metrics));

        return BacktestRun.Restore(
            new BacktestRunId(row.Id),
            new UserId(row.OwnerId),
            new StrategyId(row.StrategyId),
            new StrategyVersionNumber(row.StrategyVersion),
            row.CreatedAt.ToUniversalTime(),
            BacktestJsonSerializer.Deserialize<BacktestConfigurationDb>(row.Configuration).ToDomain(),
            row.Stage switch
            {
                null => null,
                "loading_history" => BacktestRunStage.LoadingHistory,
                "replaying" => BacktestRunStage.Replaying,
                _ => throw new InvalidOperationException($"Unknown stored run stage '{row.Stage}'.")
            },
            history,
            result);
    }

    public static string? ToDb(this BacktestRunStage? stage)
    {
        return stage switch
        {
            null => null,
            BacktestRunStage.LoadingHistory => "loading_history",
            _ => "replaying"
        };
    }

    public static string ToDb(this BacktestOutcome outcome)
    {
        return outcome switch
        {
            BacktestOutcome.Completed => "completed",
            BacktestOutcome.Cancelled => "cancelled",
            _ => "failed"
        };
    }

    private static BacktestOutcome Outcome(string stored)
    {
        return stored switch
        {
            "completed" => BacktestOutcome.Completed,
            "cancelled" => BacktestOutcome.Cancelled,
            "failed" => BacktestOutcome.Failed,
            _ => throw new InvalidOperationException($"Unknown stored run outcome '{stored}'.")
        };
    }
}