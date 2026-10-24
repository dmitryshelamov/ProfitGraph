using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     One parameter search of a strategy version (ADR 0013): the frozen configuration, then the prepared history, then
///     the stored trials, then the finalists started as ordinary runs. Not started → LoadingHistory → Searching →
///     StartingFinalists. The background job with the same identifier owns whether it is queued, running, cancelled or
///     failed.
/// </summary>
public sealed class OptimizationRun : AggregateRoot
{
    public OptimizationId Id { get; }
    public UserId OwnerId { get; }
    public StrategyId StrategyId { get; }
    public StrategyVersionNumber StrategyVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public OptimizationConfiguration Configuration { get; }
    public OptimizationStage? Stage { get; private set; }
    public BacktestHistoryInfo? History { get; private set; }
    public OptimizationCounters Counters { get; private set; } = OptimizationCounters.None;
    public OptimizationBestTrial? Best { get; private set; }

    /// <summary>
    ///     Trials the search planned to execute, known once candidates are filtered.
    /// </summary>
    public int? PlannedTrials { get; private set; }

    private OptimizationRun(
        OptimizationId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionNumber strategyVersion,
        DateTimeOffset createdAt,
        OptimizationConfiguration configuration)
    {
        Id = id;
        OwnerId = ownerId;
        StrategyId = strategyId;
        StrategyVersion = strategyVersion;
        CreatedAt = createdAt;
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public static OptimizationRun Create(
        OptimizationId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionNumber strategyVersion,
        OptimizationConfiguration configuration,
        DateTimeOffset now)
    {
        return new OptimizationRun(id, ownerId, strategyId, strategyVersion, now, configuration);
    }

    /// <summary>
    ///     Restores a stored optimization, checking the same rules its transitions check.
    /// </summary>
    public static OptimizationRun Restore(
        OptimizationId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionNumber strategyVersion,
        DateTimeOffset createdAt,
        OptimizationConfiguration configuration,
        OptimizationStage? stage,
        BacktestHistoryInfo? history,
        OptimizationCounters counters,
        OptimizationBestTrial? best,
        int? plannedTrials)
    {
        var optimization = new OptimizationRun(id, ownerId, strategyId, strategyVersion, createdAt, configuration);

        if (stage is not null)
        {
            optimization.BeginLoadingHistory();
        }

        if (history is not null)
        {
            optimization.AttachHistory(history);
        }

        if (optimization.Stage == OptimizationStage.Searching)
        {
            optimization.Restore(counters, best, plannedTrials);
        }

        if (stage == OptimizationStage.StartingFinalists)
        {
            optimization.CompleteSearch();
        }

        return optimization.Stage == stage && optimization.Counters == counters
            ? optimization
            : throw Transition("A stored optimization must have history exactly while searching or starting finalists, and trials only then.");
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    /// <summary>
    ///     Execution starts once; an optimization interrupted later is not resumed until stage 4.5.
    /// </summary>
    public void BeginLoadingHistory()
    {
        if (Stage is not null)
        {
            throw Transition("The optimization has already started.");
        }

        Stage = OptimizationStage.LoadingHistory;
    }

    public void AttachHistory(BacktestHistoryInfo history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (Stage != OptimizationStage.LoadingHistory || History is not null)
        {
            throw Transition("History is attached once, while it is being loaded.");
        }

        Configuration.Backtest.CheckHistory(history);
        History = history;
        Stage = OptimizationStage.Searching;
    }

    /// <summary>
    ///     Fixes how many trials the search executes; known once the candidates are filtered by the template rules.
    /// </summary>
    public void PlanTrials(int plannedTrials)
    {
        EnsureSearching();

        if (PlannedTrials is not null || plannedTrials < 0 || plannedTrials > Configuration.Budget || plannedTrials < Counters.Executed)
        {
            throw Transition("The planned trials are fixed once, within the budget and not below the executed trials.");
        }

        PlannedTrials = plannedTrials;
    }

    /// <summary>
    ///     Counts a stored batch of trials and keeps the best trial the caller ranked; the best is always a completed trial
    ///     with an objective.
    /// </summary>
    public void RecordTrials(IEnumerable<OptimizationTrialResult> batch, OptimizationBestTrial? best)
    {
        EnsureSearching();
        OptimizationCounters counters = Counters;

        foreach (OptimizationTrialResult trial in batch)
        {
            counters = counters.Add(trial.Status);
        }

        if (counters.Executed > Configuration.Budget || (PlannedTrials is { } planned && counters.Executed > planned) ||
            (best is null && Best is not null) || (best is not null && counters.Completed == 0))
        {
            throw Transition("Trials never exceed the plan, and the best trial is a completed one that is never dropped.");
        }

        Counters = counters;
        Best = best;
    }

    public void CompleteSearch()
    {
        EnsureSearching();
        Stage = OptimizationStage.StartingFinalists;
    }

    private void Restore(OptimizationCounters counters, OptimizationBestTrial? best, int? plannedTrials)
    {
        if (counters.Completed < 0 || counters.RejectedByConstraint < 0 || counters.RejectedByTemplate < 0 || counters.Failed < 0)
        {
            throw Transition("Trial counters cannot be negative.");
        }

        if (plannedTrials is { } planned && planned > Configuration.Budget)
        {
            throw Transition("The planned trials cannot exceed the budget.");
        }

        Counters = counters;
        PlannedTrials = plannedTrials;

        if (counters.Executed > Configuration.Budget || (best is not null && counters.Completed == 0) || (plannedTrials is { } plan && counters.Executed > plan))
        {
            throw Transition("Stored trials exceed the plan or have a best trial without a completed one.");
        }

        Best = best;
    }

    private void EnsureSearching()
    {
        if (Stage != OptimizationStage.Searching)
        {
            throw Transition("Trials are recorded only while searching.");
        }
    }

    private static OptimizationRuleException Transition(string message)
    {
        return new OptimizationRuleException(OptimizationRuleException.InvalidTransition, message);
    }
}