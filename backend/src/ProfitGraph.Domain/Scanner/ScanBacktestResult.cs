using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     What a scanner backtest found: how many pairs it replayed, the pairs whose history could not be read, the summary over every
///     signal and the signals themselves, newest first, at most <see cref="ScanBacktest.MaxStoredSignals" />.
/// </summary>
public sealed record ScanBacktestResult(
    int Pairs,
    IReadOnlyList<string> FailedPairs,
    ScanStudySummary Summary,
    IReadOnlyList<ScanSignal> Signals,
    bool SignalsTruncated);