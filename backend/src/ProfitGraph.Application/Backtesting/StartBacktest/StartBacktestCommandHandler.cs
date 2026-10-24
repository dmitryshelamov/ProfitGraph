using MediatR;

using ProfitGraph.Application.Backtesting.RunBacktest;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Strategies;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.StartBacktest;

/// <summary>
///     Freezes the run without contacting the venue: rules are captured and history is prepared by the Worker. The run
///     and its job are stored in one transaction.
/// </summary>
internal sealed class StartBacktestCommandHandler(
    IStrategyRepository strategies,
    IBacktestRunRepository runs,
    VenueRegistry venues,
    BacktestCompiler compiler,
    BackgroundJobQueue queue,
    TimeProvider clock) : IRequestHandler<StartBacktestCommand, BackgroundJobInternal>
{
    public async Task<BackgroundJobInternal> Handle(StartBacktestCommand request, CancellationToken cancellationToken)
    {
        Strategy strategy = await strategies.GetOwned(request.StrategyId, request.UserId, cancellationToken);
        StrategyVersion version = await strategies.GetVersion(
                strategy.Id,
                request.Version is { } number ? new StrategyVersionNumber(number) : strategy.CurrentVersion,
                cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");
        IReadOnlyDictionary<string, decimal> parameters = request.Parameters ?? new Dictionary<string, decimal>();
        CompiledTradingGraph plan = compiler.Compile(version.Definition, parameters);
        CandleSeries series = venues.ResolveSeries(request.Venue, request.Symbol, request.Timeframe);
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset latest = series.Timeframe.FloorToGrid(now);

        if (request.From >= latest)
        {
            throw new ApplicationValidationException([new InputError("invalid_request", "From", "The start must come before the latest closed candle.")]);
        }

        OutOfSampleSplit split = request.OutOfSampleFraction is { } fraction ? OutOfSampleSplit.Create(fraction) : OutOfSampleSplit.Default;

        if (request.From is { } from)
        {
            CheckSplit(split, ((request.To is { } to && to < latest ? to : latest).UtcTicks - from.UtcTicks) / series.Timeframe.Duration.Ticks);
        }

        var configuration = new BacktestConfiguration(
            series,
            request.From,
            request.To,
            now,
            parameters,
            new ExecutionSettings(request.StartingCapital, request.Slippage, request.LimitFeeRate, request.MarketFeeRate),
            split,
            plan.WarmupCandles,
            BacktestResult.ExecutionPolicyVersion,
            GraphCompilationFingerprint.Calculate(plan));
        var owner = new UserId(request.UserId);
        var jobId = BackgroundJobId.New();
        var run = BacktestRun.Create(new BacktestRunId(jobId), owner, strategy.Id, version.Number, configuration, now);
        BackgroundJobInternal job = await queue.Enqueue(jobId, owner, new RunBacktestCommand(jobId.Value), cancellationToken);
        await runs.Add(run, cancellationToken);

        return job;
    }

    /// <summary>
    ///     With a known start the holdout is checked at once; without one the range is known only when the Worker prepares it.
    /// </summary>
    private static void CheckSplit(OutOfSampleSplit split, long replayCount)
    {
        try
        {
            split.Split(checked((int)replayCount));
        }
        catch (Exception exception) when (exception is ArgumentException or SampleSplitException or OverflowException)
        {
            throw new ApplicationValidationException([new InputError(BacktestErrorCodes.InvalidSplit, "OutOfSampleFraction", exception.Message)]);
        }
    }
}