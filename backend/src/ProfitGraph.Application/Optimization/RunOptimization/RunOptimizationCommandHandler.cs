using MediatR;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.Backtesting.RunBacktest;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Execution;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Optimization.RunOptimization;

/// <summary>
///     Executes a frozen optimization (ADR 0013): recompiles the strategy version with the frozen ranges and checks the
///     fingerprint, prepares the history once, runs the trials on its in-sample part while storing them in batches, then
///     starts the best completed trials as ordinary runs over the whole period. A cancelled or paused optimization keeps
///     every stored trial; one that resumes continues from its stage, restores its stored trials without replaying them
///     and starts only the finalists it has not started.
/// </summary>
internal sealed class RunOptimizationCommandHandler(
    IOptimizationRunRepository optimizations,
    IOptimizationTrialStore trials,
    IOptimizationReader reader,
    IBacktestRunRepository backtests,
    IStrategyRepository strategies,
    BacktestCompiler compiler,
    BacktestHistoryLoader history,
    OptimizationTrialRunner runner,
    OptimizationTrialRunnerOptions options,
    BackgroundJobQueue queue,
    ITransactionExecutor transactions,
    IBackgroundJobProgress progress,
    TimeProvider clock) : IRequestHandler<RunOptimizationCommand, OptimizationCompletedInternal>
{
    public async Task<OptimizationCompletedInternal> Handle(RunOptimizationCommand request, CancellationToken cancellationToken)
    {
        OptimizationRun optimization = await optimizations.Get(new OptimizationId(request.OptimizationId), cancellationToken)
            ?? throw new ResourceNotFoundException("The optimization does not exist.", OptimizationErrorCodes.NotFound);
        OptimizationConfiguration configuration = optimization.Configuration;
        BacktestConfiguration backtest = configuration.Backtest;
        StrategyVersion version = await strategies.GetVersion(optimization.StrategyId, optimization.StrategyVersion, cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");
        CompiledTradingGraph plan = compiler.Compile(version.Definition, backtest.Parameters, configuration.Ranges);

        if (backtest.ExecutionPolicy != BacktestResult.ExecutionPolicyVersion || GraphCompilationFingerprint.Calculate(plan) != backtest.Fingerprint)
        {
            throw new OptimizationException(
                OptimizationErrorCodes.CompilationChanged,
                "The strategy no longer compiles with these ranges to what was frozen when the optimization started; start a new one.");
        }

        (PreparedReplay replay, InstrumentRules rules) = await PrepareHistory(optimization, cancellationToken);
        var input = new OptimizationRunInputInternal(
            version.Definition.TemplateKind,
            plan,
            replay,
            InSampleBars(backtest, replay),
            backtest.Settings,
            rules,
            OptimizationSearchSpace.FromGraph(plan),
            configuration.Objective,
            configuration.Constraints,
            configuration.Budget,
            configuration.Seed,
            configuration.Method);
        IReadOnlyList<OptimizationTrialInternal> stored = await trials.ReadAll(optimization.Id, cancellationToken);
        await progress.Restart(optimization.Counters.Executed, optimization.PlannedTrials ?? configuration.Budget, cancellationToken);
        OptimizationRunResultInternal result = await Search(optimization, input, stored, cancellationToken);

        if (result.IsCancelled)
        {
            throw new OperationCanceledException("The optimization stopped after its trials were stored.", cancellationToken);
        }

        if (optimization.Stage == OptimizationStage.Searching)
        {
            optimization.CompleteSearch();
            await Save(optimization, CancellationToken.None);
        }

        OptimizationTrialResult[] finalists = [.. result.Ranking.Where(trial => trial.Status == OptimizationTrialStatus.Completed).Take(configuration.FinalistCount)];
        IReadOnlyList<IReadOnlyDictionary<string, decimal>> started = await StartedFinalists(optimization, cancellationToken);
        await progress.Restart(completed: 0, finalists.Length, cancellationToken);

        for (var index = 0; index < finalists.Length; index++)
        {
            await StartFinalist(optimization, version, finalists[index], started, cancellationToken);
            await progress.Report(index + 1, finalists.Length, cancellationToken);
        }

        return new OptimizationCompletedInternal(optimization.Id.Value, optimization.Counters.Executed, finalists.Length);
    }

    /// <summary>
    ///     Loads history once and attaches it; an optimization that resumes prepares it again and requires the same
    ///     history, with the rules it captured (Profit Fox Designer ADR 0044).
    /// </summary>
    private async Task<(PreparedReplay Replay, InstrumentRules Rules)> PrepareHistory(OptimizationRun optimization, CancellationToken cancellationToken)
    {
        BacktestConfiguration backtest = optimization.Configuration.Backtest;

        if (optimization.Stage is null)
        {
            optimization.BeginLoadingHistory();
            await Save(optimization, cancellationToken);
        }

        if (optimization.History is not { } attached)
        {
            LoadedBacktestHistory loaded = await history.Load(backtest, cancellationToken);
            optimization.AttachHistory(loaded.Info);
            await Save(optimization, cancellationToken);

            return (loaded.Prepared.ToReplay(), loaded.Definition.Rules);
        }

        PreparedHistory prepared = await history.Prepare(backtest, cancellationToken);

        if (prepared.Identity != attached.Identity || prepared.HistoryFrom != attached.HistoryFrom || prepared.From != attached.From ||
            prepared.To != attached.To || prepared.WarmupCount != attached.WarmupCount || prepared.Candles.Count - prepared.WarmupCount != attached.ReplayCount)
        {
            throw new OptimizationException(
                OptimizationErrorCodes.HistoryMismatch,
                "The history prepared on resume no longer matches the history this optimization searched; start a new one.");
        }

        return (prepared.ToReplay(), attached.Rules);
    }

    private static int InSampleBars(BacktestConfiguration backtest, PreparedReplay replay)
    {
        try
        {
            return backtest.Split.Split(replay.Replay.Count).InSampleBars;
        }
        catch (Exception exception) when (exception is SampleSplitException or ArgumentException)
        {
            throw new OptimizationException(BacktestErrorCodes.InvalidSplit, exception.Message);
        }
    }

    /// <summary>
    ///     Runs the trials and stores them in batches on the runner's serial receiver, each batch in its own short
    ///     transaction with the counters and the best trial.
    /// </summary>
    private async Task<OptimizationRunResultInternal> Search(
        OptimizationRun optimization,
        OptimizationRunInputInternal input,
        IReadOnlyList<OptimizationTrialInternal> stored,
        CancellationToken cancellationToken)
    {
        var batch = new List<OptimizationTrialResult>(options.BatchSize);
        var planned = new OptimizationPlannedTrials();
        OptimizationTrialResult? best = OptimizationRanking.Rank(stored.Where(trial => trial is { Status: "completed", Objective: not null }).Select(Ranked))
            .FirstOrDefault();
        long sequence = stored.Count == 0 ? 0 : stored.Max(trial => trial.Sequence);
        OptimizationRunResultInternal result;

        try
        {
            result = await runner.RunAsync(
                input,
                async trial =>
                {
                    batch.Add(trial);

                    if (batch.Count >= options.BatchSize)
                    {
                        await Flush();
                    }
                },
                planned,
                cancellationToken,
                Flush,
                stored);
        }
        finally
        {
            await Flush();
        }

        if (optimization.PlannedTrials is null && !result.IsCancelled)
        {
            optimization.PlanTrials(result.TotalTrials);
            await Save(optimization, CancellationToken.None);
        }

        return result;

        async ValueTask Flush()
        {
            if (batch.Count == 0)
            {
                return;
            }

            OptimizationTrialInternal[] records = [.. batch.Select(trial => Record(++sequence, trial))];
            best = OptimizationRanking.Rank(
                    batch.Where(trial => trial is { Status: OptimizationTrialStatus.Completed, ObjectiveValue: not null }).Append(best).OfType<OptimizationTrialResult>())
                .FirstOrDefault();

            if (optimization.PlannedTrials is null && planned.Total is { } total)
            {
                optimization.PlanTrials(total);
            }

            optimization.RecordTrials(batch, best is null ? null : new OptimizationBestTrial(best.Trial.Index, best.ObjectiveValue!.Value));
            await transactions.ExecuteAsync(
                async () =>
                {
                    await trials.Write(optimization.Id, records, CancellationToken.None);
                    await optimizations.Save(optimization, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
            batch.Clear();
            await progress.RecordStoredWork(CancellationToken.None);
            await progress.Report(optimization.Counters.Executed, optimization.PlannedTrials ?? input.Budget, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Starts a finalist as an ordinary run of the whole prepared period, with the trial's values fixed (S4-5).
    /// </summary>
    private async Task StartFinalist(
        OptimizationRun optimization,
        StrategyVersion version,
        OptimizationTrialResult finalist,
        IReadOnlyList<IReadOnlyDictionary<string, decimal>> started,
        CancellationToken cancellationToken)
    {
        BacktestConfiguration backtest = optimization.Configuration.Backtest;
        BacktestHistoryInfo prepared = optimization.History!;
        var parameters = new Dictionary<string, decimal>(backtest.Parameters, StringComparer.Ordinal);

        foreach ((PortReference reference, decimal value) in finalist.Trial.Selections.OptimizedValues)
        {
            parameters[reference.NodeId] = value;
        }

        // A resumed optimization does not start a finalist it already started.
        if (started.Any(existing =>
                existing.Count == parameters.Count && existing.All(pair => parameters.TryGetValue(pair.Key, out decimal value) && value == pair.Value)))
        {
            return;
        }

        CompiledTradingGraph plan = compiler.Compile(version.Definition, parameters);
        var configuration = new BacktestConfiguration(
            backtest.Series,
            prepared.From,
            prepared.To,
            backtest.ObservedAt,
            parameters,
            backtest.Settings,
            backtest.Split,
            plan.WarmupCandles,
            BacktestResult.ExecutionPolicyVersion,
            GraphCompilationFingerprint.Calculate(plan));
        var jobId = BackgroundJobId.New();
        var run = BacktestRun.Create(
            new BacktestRunId(jobId),
            optimization.OwnerId,
            optimization.StrategyId,
            optimization.StrategyVersion,
            configuration,
            clock.GetUtcNow(),
            optimization.Id);
        await transactions.ExecuteAsync(
            async () =>
            {
                await queue.Enqueue(jobId, optimization.OwnerId, new RunBacktestCommand(jobId.Value), cancellationToken);
                await backtests.Add(run, cancellationToken);

                return true;
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, decimal>>> StartedFinalists(OptimizationRun optimization, CancellationToken cancellationToken)
    {
        var started = new List<IReadOnlyDictionary<string, decimal>>();

        foreach (Guid id in (await reader.State(optimization.Id, cancellationToken)).Finalists)
        {
            if (await backtests.Get(new BacktestRunId(id), cancellationToken) is { } run)
            {
                started.Add(run.Configuration.Parameters);
            }
        }

        return started;
    }

    /// <summary>
    ///     A stored completed trial as a rankable result: ranking reads only its objective and its values.
    /// </summary>
    private static OptimizationTrialResult Ranked(OptimizationTrialInternal stored)
    {
        var trial = new OptimizationTrial(
            stored.TrialIndex,
            GridIndex: 0,
            new EngineExecutionInputs(stored.Parameters.ToDictionary(pair => new PortReference(pair.Key, "value"), pair => pair.Value)));

        return OptimizationTrialResult.Restore(trial, metrics: null, OptimizationTrialStatus.Completed, stored.Objective, failureCode: null, stored.ElapsedMs);
    }

    private static OptimizationTrialInternal Record(long sequence, OptimizationTrialResult trial)
    {
        return new OptimizationTrialInternal(
            sequence,
            trial.Trial.Index,
            trial.Trial.Selections.OptimizedValues.ToDictionary(pair => pair.Key.NodeId, pair => pair.Value, StringComparer.Ordinal),
            OptimizationNames.TrialStatus(trial.Status),
            trial.FailureCode,
            trial.ObjectiveValue,
            trial.Metrics?.ReturnPercent,
            trial.Metrics?.TotalPnl,
            trial.Metrics?.MaxDrawdownPercent,
            trial.Metrics?.Executions ?? 0,
            trial.ElapsedMs);
    }

    private Task<bool> Save(OptimizationRun optimization, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                await optimizations.Save(optimization, cancellationToken);

                return true;
            },
            cancellationToken);
    }
}