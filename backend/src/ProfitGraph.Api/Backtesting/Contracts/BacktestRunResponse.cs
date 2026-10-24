namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     A run with its frozen configuration, prepared history and result; <paramref name="Status" /> is its job's status.
/// </summary>
public sealed record BacktestRunResponse(
    Guid Id,
    Guid StrategyId,
    int StrategyVersion,
    string Status,
    DateTimeOffset CreatedAt,
    BacktestConfigurationResponse Configuration,
    string? Stage,
    BacktestHistoryResponse? History,
    BacktestResultResponse? Result,
    long LedgerCount,
    long CandleCount,
    long DiagnosticCount,
    int? ChartBucketSize);