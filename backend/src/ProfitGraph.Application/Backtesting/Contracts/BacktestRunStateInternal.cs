namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     What the run aggregate does not hold: the job's status, the last stored observation and the stored fact counts.
/// </summary>
public sealed record BacktestRunStateInternal(
    string Status,
    string? Outcome,
    long LastSequence,
    long LedgerCount,
    long CandleCount,
    long DiagnosticCount,
    int? ChartBucketSize);