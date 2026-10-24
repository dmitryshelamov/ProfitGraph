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
    IOptimizationWindowStore windows,
    IOptimizationReader reader,
    IBacktestRunRepository backtests,
    IStrategyRepository strategies,
    BacktestCompiler compiler,
    BacktestHistoryLoader history,
    OptimizationTrialRunner runner,
    WalkForwardRunner walkForward,
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
        await progress.Restart(optimization.Counters.Executed, optimization.PlannedTrials ?? configuration.TrialCapacity, cancellationToken);
        var recorder = new OptimizationTrialRecorder(
            optimization,
            stored,
            configuration.WalkForward is null,
            options.BatchSize,
            trials,
            optimizations,
            transactions,
            progress);
        IReadOnlyList<EngineExecutionInputs> finalists = configuration.WalkForward is { } settings
            ? await SearchWalkForward(optimization, input, settings, stored, recorder, cancellationToken)
            : await Search(optimization, input, stored, recorder, cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, decimal>> started = await StartedFinalists(optimization, cancellationToken);
        await progress.Restart(completed: 0, finalists.Count, cancellationToken);

        for (var index = 0; index < finalists.Count; index++)
        {
            await StartFinalist(optimization, version, finalists[index], started, cancellationToken);
            await progress.Report(index + 1, finalists.Count, cancellationToken);
        }

        return new OptimizationCompletedInternal(optimization.Id.Value, optimization.Counters.Executed, finalists.Count);
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
    ///     Runs the trials of one search, stores them through the recorder and returns the best completed trials' values as
    ///     finalists.
    /// </summary>
    private async Task<IReadOnlyList<EngineExecutionInputs>> Search(
        OptimizationRun optimization,
        OptimizationRunInputInternal input,
        IReadOnlyList<OptimizationTrialInternal> stored,
        OptimizationTrialRecorder recorder,
        CancellationToken cancellationToken)
    {
        OptimizationRunResultInternal result;

        try
        {
            result = await runner.RunAsync(input, trial => recorder.Add(windowIndex: 0, trial), recorder.Planned, cancellationToken, recorder.Flush, stored);
        }
        finally
        {
            await recorder.Flush();
        }

        if (result.IsCancelled)
        {
            throw new OperationCanceledException("The optimization stopped after its trials were stored.", cancellationToken);
        }

        if (optimization.Stage == OptimizationStage.Searching)
        {
            optimization.PlanTrials(result.TotalTrials);
            optimization.CompleteSearch();
            await transactions.ExecuteAsync(
                async () =>
                {
                    await trials.WriteScores(optimization.Id, windowIndex: 0, result.NeighborhoodScores, CancellationToken.None);
                    await optimizations.Save(optimization, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
        }

        return
        [
            .. result.Ranking.Where(trial => trial.Status == OptimizationTrialStatus.Completed)
                .Take(optimization.Configuration.FinalistCount)
                .Select(trial => trial.Trial.Selections)
        ];
    }

    /// <summary>
    ///     Searches each walk-forward window's train range and tests its winner (ADR 0014), storing trials, finished windows
    ///     and scores as they come; the finalist is the last window's winner.
    /// </summary>
    private async Task<IReadOnlyList<EngineExecutionInputs>> SearchWalkForward(
        OptimizationRun optimization,
        OptimizationRunInputInternal input,
        WalkForwardSettings settings,
        IReadOnlyList<OptimizationTrialInternal> stored,
        OptimizationTrialRecorder recorder,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WalkForwardWindowResult> finished = await windows.ReadWindows(optimization.Id, cancellationToken);
        WalkForwardRunResultInternal result;

        try
        {
            result = await walkForward.RunAsync(
                input,
                settings,
                trial => recorder.Add(trial.WindowIndex, trial.Result),
                async window =>
                {
                    await recorder.Flush();
                    await Store(() => windows.WriteWindow(optimization.Id, window, CancellationToken.None));
                },
                recorder.Planned,
                cancellationToken,
                async (windowIndex, scores) =>
                {
                    await recorder.Flush();
                    await Store(() => trials.WriteScores(optimization.Id, windowIndex, scores, CancellationToken.None));
                },
                recorder.Flush,
                stored,
                finished);
        }
        catch (WalkForwardWindowException exception)
        {
            throw new OptimizationException(exception.Code, exception.Message);
        }
        finally
        {
            await recorder.Flush();
        }

        if (result.IsCancelled)
        {
            throw new OperationCanceledException("The walk-forward optimization stopped after its trials and windows were stored.", cancellationToken);
        }

        if (optimization.Stage == OptimizationStage.Searching)
        {
            optimization.PlanTrials(result.TotalTrials);
            optimization.CompleteSearch();
            await Store(async () =>
            {
                await windows.WriteSummary(optimization.Id, result.Summary, CancellationToken.None);
                await optimizations.Save(optimization, CancellationToken.None);
            });
        }

        return
        [
            .. result.Windows.Select(window => window.Parameters)
                .TakeLast(1)
                .OfType<EngineExecutionInputs>()
                .Take(optimization.Configuration.FinalistCount)
        ];
    }

    private Task<bool> Store(Func<Task> write)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                await write();

                return true;
            },
            CancellationToken.None);
    }

    /// <summary>
    ///     Starts a finalist as an ordinary run of the whole prepared period, with the trial's values fixed (S4-5).
    /// </summary>
    private async Task StartFinalist(
        OptimizationRun optimization,
        StrategyVersion version,
        EngineExecutionInputs finalist,
        IReadOnlyList<IReadOnlyDictionary<string, decimal>> started,
        CancellationToken cancellationToken)
    {
        BacktestConfiguration backtest = optimization.Configuration.Backtest;
        BacktestHistoryInfo prepared = optimization.History!;
        var parameters = new Dictionary<string, decimal>(backtest.Parameters, StringComparer.Ordinal);

        foreach ((PortReference reference, decimal value) in finalist.OptimizedValues)
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