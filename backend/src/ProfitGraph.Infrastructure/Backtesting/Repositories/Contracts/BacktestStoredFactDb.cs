namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     A stored ledger entry or diagnostic: its sequence, the order it concerns and its JSON snapshot.
/// </summary>
internal sealed class BacktestStoredFactDb
{
    public long Sequence { get; init; }
    public long? OrderId { get; init; }
    public string Snapshot { get; init; } = null!;
}