namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

internal sealed class BacktestRunStateDb
{
    public string Status { get; init; } = null!;
    public string? Outcome { get; init; }
    public long LastSequence { get; init; }
    public long LedgerCount { get; init; }
    public long CandleCount { get; init; }
    public long DiagnosticCount { get; init; }
    public int? ChartBucketSize { get; init; }
}