using ProfitGraph.Api.Backtesting.Contracts;

namespace ProfitGraph.Api.Optimization.Contracts;

/// <param name="Finalists">The finalist backtest runs, oldest first; each is an ordinary run at /api/backtests/{id}.</param>
public sealed record OptimizationRunResponse(
    Guid Id,
    Guid StrategyId,
    int StrategyVersion,
    string Status,
    DateTimeOffset CreatedAt,
    OptimizationConfigurationResponse Configuration,
    string? Stage,
    BacktestHistoryResponse? History,
    int? PlannedTrials,
    OptimizationCountersResponse Counters,
    OptimizationBestTrialResponse? Best,
    IReadOnlyList<Guid> Finalists,
    WalkForwardSummaryResponse? WalkForwardSummary);