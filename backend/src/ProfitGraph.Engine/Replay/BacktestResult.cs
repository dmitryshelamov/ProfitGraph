namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Copied replay facts and terminal outcome; default execution does not retain candle or dispatch projections.
/// </summary>
public sealed class BacktestResult
{
    public const string ExecutionPolicyVersion = "spot-olhc-cascade10000-v2";
    public BacktestTerminalSnapshot Terminal { get; }
    public IReadOnlyList<BacktestLedgerEntry> Ledger { get; }
    public IReadOnlyList<BacktestDiagnostic> Diagnostics { get; }
    public SampleRanges SampleRanges { get; }
    public long LedgerCount { get; }
    public int FillCount { get; }
    public int OrderCount { get; }

    public BacktestResult(
        BacktestTerminalSnapshot terminal,
        IEnumerable<BacktestLedgerEntry> ledger,
        IEnumerable<BacktestDiagnostic> diagnostics,
        SampleRanges sampleRanges,
        long? ledgerCount = null,
        int? fillCount = null,
        int? orderCount = null)
    {
        Terminal = terminal;
        Ledger = Array.AsReadOnly(ledger.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        SampleRanges = sampleRanges;
        LedgerCount = ledgerCount ?? Ledger.Count;
        FillCount = fillCount ?? Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Filled);
        OrderCount = orderCount ?? Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Submitted);
    }
}