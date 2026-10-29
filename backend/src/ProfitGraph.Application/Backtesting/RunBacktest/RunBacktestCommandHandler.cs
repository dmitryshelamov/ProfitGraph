using MediatR;

using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.RunBacktest;

/// <summary>
///     Executes a frozen run (ADR 0012): recompiles the strategy version and checks the frozen fingerprint, captures the
///     instrument rules, prepares the history, replays it while storing facts, and stores the result. A cancelled or
///     failed replay keeps every fact it stored; the job records how it ended.
/// </summary>
internal sealed class RunBacktestCommandHandler(
    IBacktestRunRepository runs,
    IStrategyRepository strategies,
    BacktestCompiler compiler,
    BacktestHistoryLoader history,
    MarketDataCredentials credentials,
    IBacktestFactStore facts,
    ITransactionExecutor transactions,
    IBackgroundJobProgress progress,
    BacktestExecutionOptions options,
    VenuePorts<IVenueRestingOrderCapacity> capacities) : IRequestHandler<RunBacktestCommand, BacktestCompletedInternal>
{
    private const string GridVenueOrdersUnmodelled = "GridVenueOrdersUnmodelled";

    public async Task<BacktestCompletedInternal> Handle(RunBacktestCommand request, CancellationToken cancellationToken)
    {
        BacktestRun run = await runs.Get(new BacktestRunId(request.RunId), cancellationToken)
            ?? throw new ResourceNotFoundException("The backtest run does not exist.", BacktestErrorCodes.RunNotFound);
        BacktestConfiguration configuration = run.Configuration;
        CompiledTradingGraph plan = await Compile(run, cancellationToken);
        run.BeginLoadingHistory();
        await Save(run, cancellationToken);

        LoadedBacktestHistory loaded;

        using (MarketDataAccess.Use(await credentials.ReadIfStillThere(run.OwnerId.Value, request.CredentialId, configuration.Series.Venue, cancellationToken)))
        {
            loaded = await history.Load(configuration, cancellationToken);
        }

        run.AttachHistory(loaded.Info);
        await Save(run, cancellationToken);

        BacktestInput input = Input(plan, configuration, loaded.Definition, loaded.Prepared);
        await progress.Restart(completed: 0, input.SampleRanges.TotalBars, cancellationToken);
        var session = new BacktestReplaySession(run.Id, facts, transactions, progress, options, configuration.Settings.StartingCapital, input.SampleRanges);

        // Replay holds every resting order a grid wants; a venue that cannot run grids may hold fewer (ADR 0058, section 9).
        if (capacities.Find(configuration.Series.Venue) is null && GridStatics.Read(plan, EngineExecutionInputs.Empty).Count > 0)
        {
            session.OnDiagnostic(
                new BacktestDiagnostic(
                    GridVenueOrdersUnmodelled,
                    "This venue does not run grids live; the backtest assumes it holds every resting order the grid wants."));
        }

        // The replay is synchronous and waits for room in the session's channel, so it gets its own thread.
        BacktestResult result = await Task.Factory.StartNew(
            () => new BacktestEngine().Run(input, session, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        BacktestRunMetrics? metrics = await session.Complete();
        BacktestTerminalSnapshot terminal = result.Terminal;
        run.Finish(
            new BacktestRunResult(
                terminal.Outcome,
                terminal.ProcessedCandles,
                terminal.Equity,
                terminal.TotalPnl,
                terminal.Outcome == BacktestOutcome.Failed ? BacktestErrorCodes.ReplayFailed : null,
                terminal.Outcome == BacktestOutcome.Failed ? terminal.Diagnostic?.Message ?? "The replay failed." : null,
                metrics));
        await Save(run, CancellationToken.None);

        return terminal.Outcome switch
        {
            BacktestOutcome.Completed => new BacktestCompletedInternal(request.RunId, terminal.ProcessedCandles, terminal.TotalPnl),
            BacktestOutcome.Cancelled => throw new OperationCanceledException("The replay was cancelled after its facts were stored.", cancellationToken),
            _ => throw new BacktestException(BacktestErrorCodes.ReplayFailed, terminal.Diagnostic?.Message ?? "The replay failed after storing its facts.")
        };
    }

    private async Task<CompiledTradingGraph> Compile(BacktestRun run, CancellationToken cancellationToken)
    {
        StrategyVersion version = await strategies.GetVersion(run.StrategyId, run.StrategyVersion, cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");
        CompiledTradingGraph plan = compiler.Compile(version.Definition, run.Configuration.Parameters);

        return run.Configuration.ExecutionPolicy == BacktestResult.ExecutionPolicyVersion &&
            GraphCompilationFingerprint.Calculate(plan) == run.Configuration.Fingerprint
                ? plan
                : throw new BacktestException(
                    BacktestErrorCodes.CompilationChanged,
                    "The strategy no longer compiles to what was frozen when the run started; start a new run.");
    }

    private static BacktestInput Input(
        CompiledTradingGraph plan,
        BacktestConfiguration configuration,
        InstrumentRulesDefinitionInternal definition,
        PreparedHistory prepared)
    {
        try
        {
            return new BacktestInput(plan, EngineExecutionInputs.Empty, configuration.Settings, definition.Rules, prepared.ToReplay(), configuration.Split);
        }
        catch (Exception exception) when (exception is SampleSplitException or ArgumentException)
        {
            throw new BacktestException(BacktestErrorCodes.InvalidSplit, exception.Message);
        }
        catch (BacktestInputException exception)
        {
            throw new BacktestException(BacktestErrorCodes.StrategyInvalid, exception.Message);
        }
    }

    private Task<bool> Save(BacktestRun run, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                await runs.Save(run, cancellationToken);

                return true;
            },
            cancellationToken);
    }
}