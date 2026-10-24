using ProfitGraph.Application.Backtesting.StartBacktest;
using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.StartOptimization;

/// <summary>
///     Freezes a parameter search around a backtest and queues it (ADR 0013). Ranges are by parameter name; the backtest's
///     parameters stay fixed. The method <c>auto</c> enumerates a grid within the budget and samples a larger one; the
///     defaults are a budget of 2000, three finalists, at least one execution and at most 50 % drawdown.
/// </summary>
public sealed record StartOptimizationCommand(
    StartBacktestCommand Backtest,
    IReadOnlyDictionary<string, OptimizationRangeInternal> Ranges,
    string? Method,
    string? Objective,
    int? Budget,
    ulong? Seed,
    int? FinalistCount,
    long? MinExecutions,
    decimal? MaxDrawdownPercent) : ICommand<BackgroundJobInternal>;