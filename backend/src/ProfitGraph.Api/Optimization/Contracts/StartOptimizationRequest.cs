using ProfitGraph.Api.Backtesting.Contracts;

namespace ProfitGraph.Api.Optimization.Contracts;

/// <summary>
///     Starts a parameter search around a backtest; ranges are by parameter name and decimals are exact invariant strings,
///     the seed included. Omitted values take the defaults: method <c>auto</c>, in-sample return, a budget of 2000, three
///     finalists, at least one execution and at most 50 % drawdown.
/// </summary>
public sealed record StartOptimizationRequest(
    StartBacktestRequest? Backtest,
    IReadOnlyDictionary<string, OptimizationRangeRequest>? Ranges,
    string? Method,
    string? Objective,
    int? Budget,
    string? Seed,
    int? FinalistCount,
    long? MinExecutions,
    string? MaxDrawdownPercent);