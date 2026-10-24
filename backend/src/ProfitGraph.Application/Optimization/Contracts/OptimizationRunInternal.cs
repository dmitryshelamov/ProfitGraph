using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Contracts;

public sealed record OptimizationRunInternal(
    Guid Id,
    Guid StrategyId,
    int StrategyVersion,
    DateTimeOffset CreatedAt,
    OptimizationConfigurationInternal Configuration,
    string? Stage,
    BacktestHistoryInternal? History,
    int? PlannedTrials,
    OptimizationCountersInternal Counters,
    OptimizationBestTrialInternal? Best,
    OptimizationStateInternal State,
    WalkForwardSummary? WalkForwardSummary);