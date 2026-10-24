using System.Buffers.Binary;
using System.Security.Cryptography;

using MediatR;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Backtesting.StartBacktest;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.RunOptimization;
using ProfitGraph.Application.Strategies;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Application.Optimization.StartOptimization;

/// <summary>
///     Freezes the optimization without contacting the venue: the Worker captures rules and prepares history. The
///     optimization and its job are stored in one transaction.
/// </summary>
internal sealed class StartOptimizationCommandHandler(
    IStrategyRepository strategies,
    IOptimizationRunRepository optimizations,
    VenueRegistry venues,
    BacktestCompiler compiler,
    BackgroundJobQueue queue,
    TimeProvider clock) : IRequestHandler<StartOptimizationCommand, BackgroundJobInternal>
{
    public async Task<BackgroundJobInternal> Handle(StartOptimizationCommand request, CancellationToken cancellationToken)
    {
        StartBacktestCommand backtest = request.Backtest;
        Strategy strategy = await strategies.GetOwned(backtest.StrategyId, backtest.UserId, cancellationToken);
        StrategyVersion version = await strategies.GetVersion(
                strategy.Id,
                backtest.Version is { } number ? new StrategyVersionNumber(number) : strategy.CurrentVersion,
                cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");
        IReadOnlyDictionary<string, decimal> parameters = backtest.Parameters ?? new Dictionary<string, decimal>();
        Dictionary<string, DecimalOptimizeParamValue> ranges = request.Ranges.ToDictionary(
            pair => pair.Key,
            pair => new DecimalOptimizeParamValue(pair.Value.Min, pair.Value.Max, pair.Value.Step),
            StringComparer.Ordinal);
        CompiledTradingGraph plan = compiler.Compile(version.Definition, parameters, ranges);
        OptimizationSearchSpace space = Rule(() => OptimizationSearchSpace.FromGraph(plan), "Ranges");
        int budget = request.Budget ?? OptimizationSearchSpace.DefaultBudget;
        string method = request.Method ?? OptimizationNames.Auto;

        if (method == "grid" && space.GridSize > budget)
        {
            throw Invalid("Budget", $"The grid has {space.GridSize} candidates, more than the budget; use auto or random.");
        }

        bool coarseApplicable = OptimizationCoarseToFinePlan.TryCreate(space, budget, out _);
        bool tpeApplicable = OptimizationTpePlan.TryCreate(space, budget, out _);

        if (method == "coarse_to_fine" && !coarseApplicable)
        {
            throw Invalid("Method", "Coarse-to-fine search needs a coarse grid of at least two candidates within a third of the budget.");
        }

        if (method == "tpe" && !tpeApplicable)
        {
            throw Invalid("Method", "TPE search needs a grid larger than the budget and a budget of at least 50.");
        }

        OptimizationConstraints constraints = Rule(
            () => new OptimizationConstraints(request.MinExecutions ?? 1, request.MaxDrawdownPercent ?? 50m),
            "MaxDrawdownPercent");
        CandleSeries series = venues.ResolveSeries(backtest.Venue, backtest.Symbol, backtest.Timeframe);
        BacktestConfiguration frozen = BacktestFreezing.Freeze(backtest, series, parameters, plan, clock.GetUtcNow());
        WalkForwardSettings? walkForward = request.WalkForward is { } requested ? WalkForward(requested, frozen) : null;
        var configuration = new OptimizationConfiguration(
            frozen,
            ranges,
            method == OptimizationNames.Auto
                ? space.GridSize <= budget ? OptimizationSearchMethod.Grid :
                coarseApplicable ? OptimizationSearchMethod.CoarseToFine :
                tpeApplicable ? OptimizationSearchMethod.Tpe : OptimizationSearchMethod.Random
                : OptimizationNames.ParseMethod(method),
            request.Objective is { } objective ? OptimizationNames.ParseObjective(objective) : OptimizationObjective.InSampleReturnPercent,
            constraints,
            budget,
            request.Seed ?? BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(count: 8)),
            request.FinalistCount ?? (walkForward is null ? 3 : 1),
            walkForward);
        var owner = new UserId(backtest.UserId);
        var jobId = BackgroundJobId.New();
        var optimization = OptimizationRun.Create(new OptimizationId(jobId), owner, strategy.Id, version.Number, configuration, frozen.ObservedAt);
        BackgroundJobInternal job = await queue.Enqueue(jobId, owner, new RunOptimizationCommand(jobId.Value), cancellationToken);
        await optimizations.Add(optimization, cancellationToken);

        return job;
    }

    /// <summary>
    ///     With a known start the windows are checked at once against the in-sample range; without one only the Worker
    ///     knows the range.
    /// </summary>
    private static WalkForwardSettings WalkForward(WalkForwardSettingsInternal requested, BacktestConfiguration frozen)
    {
        try
        {
            var settings = new WalkForwardSettings(
                requested.Windows,
                requested.TrainToTestRatio,
                requested.Scheme == "anchored" ? WalkForwardScheme.Anchored : WalkForwardScheme.Rolling);

            if (frozen.From is { } from)
            {
                long replay = (frozen.ResolveTo().UtcTicks - from.UtcTicks) / frozen.Series.Timeframe.Duration.Ticks;
                WalkForwardPlan.Create(frozen.Split.Split(checked((int)replay)).InSampleBars, settings);
            }

            return settings;
        }
        catch (WalkForwardWindowException exception)
        {
            throw new ApplicationValidationException([new InputError(exception.Code, "WalkForward", exception.Message)]);
        }
    }

    private static T Rule<T>(Func<T> create, string field)
    {
        try
        {
            return create();
        }
        catch (OptimizationRuleException exception)
        {
            throw Invalid(field, exception.Message);
        }
    }

    private static ApplicationValidationException Invalid(string field, string message)
    {
        return new ApplicationValidationException([new InputError(OptimizationErrorCodes.InvalidOptimization, field, message)]);
    }
}