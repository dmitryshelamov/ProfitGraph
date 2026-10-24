using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     Facts of a run committed together: ledger entries, candle observations and diagnostics, each in replay order.
/// </summary>
public sealed record BacktestFactBatch(
    IReadOnlyList<BacktestLedgerEntry> Ledger,
    IReadOnlyList<BacktestCandleObservation> Candles,
    IReadOnlyList<BacktestDiagnosticRecord> Diagnostics);