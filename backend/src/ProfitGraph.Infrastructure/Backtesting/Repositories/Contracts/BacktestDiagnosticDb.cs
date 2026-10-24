using System.Text.Json;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     A <c>backtest_diagnostics</c> row as it is sent to the database.
/// </summary>
internal sealed class BacktestDiagnosticDb
{
    public long Sequence { get; init; }
    public string Code { get; init; } = null!;
    public int? CandleIndex { get; init; }
    public JsonElement Snapshot { get; init; }
}