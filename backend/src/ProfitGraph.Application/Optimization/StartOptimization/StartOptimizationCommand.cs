using ProfitGraph.Application.Backtesting.StartBacktest;
using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Application.Optimization.StartOptimization;

/// <summary>
///     Freezes a parameter search around a backtest and queues it (ADR 0013). Ranges are by parameter name; the backtest's
///     parameters stay fixed. The method <c>auto</c> enumerates a grid within the budget; the
///     defaults are a budget of 2000, three finalists, at least one execution and at most 50 % drawdown. A larger grid
///     is searched coarse-to-fine when that applies, else by TPE, else by random sampling (Profit Fox Designer ADR 0033).
///     With walk-forward settings every window searches its train range with the budget and the finalist is the last
///     window's winner (ADR 0014); omitted walk-forward values are six rolling windows with a train range four times its
///     test range.
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
    decimal? MaxDrawdownPercent,
    WalkForwardSettingsInternal? WalkForward = null) : ICommand<BackgroundJobInternal>;