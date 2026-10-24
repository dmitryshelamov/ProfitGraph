using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     One backtest of a strategy version (ADR 0012): the frozen configuration, then the prepared history, then the
///     result. Not started → LoadingHistory → Replaying → finished with a result. The background job with the same
///     identifier owns whether the run is queued, running, cancelled or failed.
/// </summary>
public sealed class BacktestRun : AggregateRoot
{
    public BacktestRunId Id { get; }
    public UserId OwnerId { get; }
    public StrategyId StrategyId { get; }
    public StrategyVersionNumber StrategyVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public BacktestConfiguration Configuration { get; }
    public BacktestRunStage? Stage { get; private set; }
    public BacktestHistoryInfo? History { get; private set; }
    public BacktestRunResult? Result { get; private set; }

    private BacktestRun(
        BacktestRunId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionNumber strategyVersion,
        DateTimeOffset createdAt,
        BacktestConfiguration configuration)
    {
        Id = id;
        OwnerId = ownerId;
        StrategyId = strategyId;
        StrategyVersion = strategyVersion;
        CreatedAt = createdAt;
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public static BacktestRun Create(
        BacktestRunId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionNumber strategyVersion,
        BacktestConfiguration configuration,
        DateTimeOffset now)
    {
        return new BacktestRun(id, ownerId, strategyId, strategyVersion, now, configuration);
    }

    /// <summary>
    ///     Restores a stored run, checking the same rules its transitions check.
    /// </summary>
    public static BacktestRun Restore(
        BacktestRunId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionNumber strategyVersion,
        DateTimeOffset createdAt,
        BacktestConfiguration configuration,
        BacktestRunStage? stage,
        BacktestHistoryInfo? history,
        BacktestRunResult? result)
    {
        var run = new BacktestRun(id, ownerId, strategyId, strategyVersion, createdAt, configuration);

        if (stage is not null)
        {
            run.BeginLoadingHistory();
        }

        if (history is not null)
        {
            run.AttachHistory(history);
        }

        if (result is not null)
        {
            run.Finish(result);
        }

        return run.Stage == stage
            ? run
            : throw new BacktestRunRuleException(BacktestRunRuleException.InvalidTransition, "A stored run must have history exactly while replaying.");
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    /// <summary>
    ///     Execution starts once; a run interrupted later is never resumed (ADR 0011).
    /// </summary>
    public void BeginLoadingHistory()
    {
        if (Stage is not null)
        {
            throw new BacktestRunRuleException(BacktestRunRuleException.InvalidTransition, "The run has already started.");
        }

        Stage = BacktestRunStage.LoadingHistory;
    }

    public void AttachHistory(BacktestHistoryInfo history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (Stage != BacktestRunStage.LoadingHistory || History is not null)
        {
            throw new BacktestRunRuleException(BacktestRunRuleException.InvalidTransition, "History is attached once, while it is being loaded.");
        }

        Timeframe timeframe = Configuration.Series.Timeframe;
        long step = timeframe.Duration.Ticks;

        if (string.IsNullOrWhiteSpace(history.Identity) || history.Rules is null ||
            string.IsNullOrWhiteSpace(history.BaseAsset) || string.IsNullOrWhiteSpace(history.QuoteAsset) || history.BaseAsset == history.QuoteAsset)
        {
            throw InvalidHistory("History needs an identity and the captured rules of a pair of two assets.");
        }

        if ((Configuration.From is { } from && history.From != from) || history.To != Configuration.ResolveTo() ||
            history.HistoryFrom.Offset != TimeSpan.Zero || history.From.Offset != TimeSpan.Zero || history.To.Offset != TimeSpan.Zero ||
            !timeframe.IsAligned(history.HistoryFrom) || !timeframe.IsAligned(history.From) || history.HistoryFrom > history.From || history.From >= history.To)
        {
            throw InvalidHistory("History must cover the frozen period on the timeframe grid.");
        }

        if (history.WarmupCount < 0 || history.WarmupCount > Configuration.WarmupCandles ||
            (history.From.UtcTicks - history.HistoryFrom.UtcTicks) / step != history.WarmupCount ||
            (history.To.UtcTicks - history.From.UtcTicks) / step != history.ReplayCount)
        {
            throw InvalidHistory("Warm-up and replay counts must match the attached ranges and the strategy's demand.");
        }

        History = history;
        Stage = BacktestRunStage.Replaying;
    }

    public void Finish(BacktestRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Stage != BacktestRunStage.Replaying || Result is not null)
        {
            throw new BacktestRunRuleException(BacktestRunRuleException.InvalidTransition, "A result is stored once, after history was attached.");
        }

        if (!Enum.IsDefined(result.Outcome) || result.ProcessedCandles < 0 || result.ProcessedCandles > History!.ReplayCount ||
            (result.Outcome == BacktestOutcome.Completed && result.ProcessedCandles != History.ReplayCount) ||
            result.Outcome == BacktestOutcome.Failed != result.FailureCode is not null ||
            result.Equity is null != result.TotalPnl is null ||
            (result.Metrics is not null && result.Metrics.Full.ObservedCandles != result.ProcessedCandles))
        {
            throw new BacktestRunRuleException(BacktestRunRuleException.InvalidResult, "The result does not match the replay it ends.");
        }

        Result = result;
    }

    private static BacktestRunRuleException InvalidHistory(string message)
    {
        return new BacktestRunRuleException(BacktestRunRuleException.InvalidHistory, message);
    }
}