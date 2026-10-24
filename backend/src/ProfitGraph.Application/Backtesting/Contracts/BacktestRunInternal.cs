namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestRunInternal(
    Guid Id,
    Guid StrategyId,
    int StrategyVersion,
    DateTimeOffset CreatedAt,
    BacktestConfigurationInternal Configuration,
    string? Stage,
    BacktestHistoryInternal? History,
    BacktestResultInternal? Result,
    BacktestRunStateInternal State);