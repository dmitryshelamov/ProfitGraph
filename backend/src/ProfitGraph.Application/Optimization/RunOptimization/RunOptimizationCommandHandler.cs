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
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Optimization.RunOptimization;

/// <summary>
///     Executes a frozen optimization (ADR 0013): recompiles the strategy version with the frozen ranges and checks the
///     fingerprint, prepares the history once, runs the trials on its in-sample part while storing them in batches, then
///     starts the best completed trials as ordinary runs over the whole period. A cancelled optimization keeps every
///     stored trial.
/// </summary>
internal sealed class RunOptimizationCommandHandler(
    IOptimizationRunRepository optimizations,
    IOptimizationTrialStore trials,
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

        optimization.BeginLoadingHistory();
        await Save(optimization, cancellationToken);
        LoadedBacktestHistory loaded = await history.Load(backtest, cancellationToken);
        optimization.AttachHistory(loaded.Info);
        await Save(optimization, cancellationToken);

        PreparedReplay replay = loaded.Prepared.ToReplay();
        var input = new OptimizationRunInputInternal(
            version.Definition.TemplateKind,
            plan,
            replay,
            InSampleBars(backtest, replay),
            backtest.Settings,
            loaded.Definition.Rules,
            OptimizationSearchSpace.FromGraph(plan),
            configuration.Objective,
            configuration.Constraints,
            configuration.Budget,
            configuration.Seed,
            configuration.Method);
        await progress.Restart(completed: 0, configuration.Budget, cancellationToken);
        OptimizationRunResultInternal result = await Search(optimization, input, cancellationToken);

        if (result.IsCancelled)
        {
            throw new OperationCanceledException("The optimization was cancelled after its trials were stored.", cancellationToken);
        }

        optimization.CompleteSearch();
        await Save(optimization, CancellationToken.None);
        OptimizationTrialResult[] finalists = [.. result.Ranking.Where(trial => trial.Status == OptimizationTrialStatus.Completed).Take(configuration.FinalistCount)];
        await progress.Restart(completed: 0, finalists.Length, cancellationToken);

        for (var index = 0; index < finalists.Length; index++)
        {
            await StartFinalist(optimization, version, finalists[index], cancellationToken);
            await progress.Report(index + 1, finalists.Length, cancellationToken);
        }

        return new OptimizationCompletedInternal(optimization.Id.Value, optimization.Counters.Executed, finalists.Length);
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
    private async Task<OptimizationRunResultInternal> Search(OptimizationRun optimization, OptimizationRunInputInternal input, CancellationToken cancellationToken)
    {
        var batch = new List<OptimizationTrialResult>(options.BatchSize);
        var planned = new OptimizationPlannedTrials();
        OptimizationTrialResult? best = null;
        long sequence = 0;
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
                Flush);
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
            await progress.Report(optimization.Counters.Executed, optimization.PlannedTrials ?? input.Budget, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Starts a finalist as an ordinary run of the whole prepared period, with the trial's values fixed (S4-5).
    /// </summary>
    private async Task StartFinalist(OptimizationRun optimization, StrategyVersion version, OptimizationTrialResult finalist, CancellationToken cancellationToken)
    {
        BacktestConfiguration backtest = optimization.Configuration.Backtest;
        BacktestHistoryInfo prepared = optimization.History!;
        var parameters = new Dictionary<string, decimal>(backtest.Parameters, StringComparer.Ordinal);

        foreach ((PortReference reference, decimal value) in finalist.Trial.Selections.OptimizedValues)
        {
            parameters[reference.NodeId] = value;
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