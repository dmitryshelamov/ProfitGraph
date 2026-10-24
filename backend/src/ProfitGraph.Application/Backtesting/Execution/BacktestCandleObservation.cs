using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Execution;

/// <summary>
///     A processed replay candle with the account after it, and the ledger and diagnostic sequences reported with it:
///     (<paramref name="LedgerFrom" />, <paramref name="LedgerThrough" />] and (<paramref name="DiagnosticFrom" />,
///     <paramref name="DiagnosticThrough" />].
/// </summary>
public sealed record BacktestCandleObservation(
    CandleSnapshot Snapshot,
    long LedgerFrom,
    long LedgerThrough,
    long DiagnosticFrom,
    long DiagnosticThrough);