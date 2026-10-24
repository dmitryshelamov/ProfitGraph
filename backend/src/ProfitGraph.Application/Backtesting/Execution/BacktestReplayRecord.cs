using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     One fact the engine reported, waiting in the session's channel; exactly one member is set.
/// </summary>
internal sealed record BacktestReplayRecord(BacktestLedgerEntry? Ledger = null, CandleSnapshot? Candle = null, BacktestDiagnostic? Diagnostic = null);