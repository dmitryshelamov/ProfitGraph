namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Receives immutable committed facts, completed candles and a separate terminal callback; no cancellation token suppresses terminal delivery.
/// </summary>
public interface IBacktestObserver
{
    void OnLedger(BacktestLedgerEntry entry);
    void OnCandleProcessed(CandleSnapshot snapshot);
    void OnDiagnostic(BacktestDiagnostic diagnostic);
    void OnTerminal(BacktestTerminalSnapshot terminal);
}