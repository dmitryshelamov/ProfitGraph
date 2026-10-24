using System.Text.Json;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     A <c>backtest_ledger</c> row as it is sent to the database.
/// </summary>
internal sealed class BacktestLedgerDb
{
    public long Sequence { get; init; }
    public int CandleIndex { get; init; }
    public string Kind { get; init; } = null!;
    public long? OrderId { get; init; }
    public JsonElement Snapshot { get; init; }
}