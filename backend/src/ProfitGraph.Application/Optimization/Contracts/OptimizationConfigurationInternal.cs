using ProfitGraph.Application.Backtesting.Contracts;

namespace ProfitGraph.Application.Optimization.Contracts;

public sealed record OptimizationConfigurationInternal(
    BacktestConfigurationInternal Backtest,
    IReadOnlyDictionary<string, OptimizationRangeInternal> Ranges,
    string Method,
    string Objective,
    int Budget,
    ulong Seed,
    int FinalistCount,
    long MinExecutions,
    decimal MaxDrawdownPercent);