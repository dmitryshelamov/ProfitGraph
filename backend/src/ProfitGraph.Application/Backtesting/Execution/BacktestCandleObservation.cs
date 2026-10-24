using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     A processed replay candle with the account after it, and how far the ledger and diagnostics had got by then, so a
///     reader can take the facts of each candle from the stored facts.
/// </summary>
public sealed record BacktestCandleObservation(CandleSnapshot Snapshot, long LedgerThrough, long DiagnosticThrough);