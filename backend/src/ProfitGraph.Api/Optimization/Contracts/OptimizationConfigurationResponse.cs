using ProfitGraph.Api.Backtesting.Contracts;

namespace ProfitGraph.Api.Optimization.Contracts;

public sealed record OptimizationConfigurationResponse(
    BacktestConfigurationResponse Backtest,
    IReadOnlyDictionary<string, OptimizationRangeResponse> Ranges,
    string Method,
    string Objective,
    int Budget,
    string Seed,
    int FinalistCount,
    long MinExecutions,
    string MaxDrawdownPercent);