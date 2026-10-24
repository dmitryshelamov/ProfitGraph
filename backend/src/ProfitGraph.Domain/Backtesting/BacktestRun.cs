using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Strategies;
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

    /// <summary>
    ///     The optimization that started this run as one of its finalists.
    /// </summary>
    public OptimizationId? OptimizationId { get; }

    public BacktestRunStage? Stage { get; private set; }
    public BacktestHistoryInfo? History { get; private set; }
    public BacktestRunResult? Result { get; private set; }

    private BacktestRun(
        BacktestRunId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionNumber strategyVersion,
        DateTimeOffset createdAt,
        BacktestConfiguration configuration,
        OptimizationId? optimizationId)
    {
        Id = id;
        OptimizationId = optimizationId;
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
        DateTimeOffset now,
        OptimizationId? optimizationId = null)
    {
        return new BacktestRun(id, ownerId, strategyId, strategyVersion, now, configuration, optimizationId);
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
        BacktestRunResult? result,
        OptimizationId? optimizationId = null)
    {
        var run = new BacktestRun(id, ownerId, strategyId, strategyVersion, createdAt, configuration, optimizationId);

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

        Configuration.CheckHistory(history);
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
}