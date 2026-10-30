using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     What a scanner backtest found: how many pairs it replayed, the pairs whose history could not be read, the summary over every
///     signal and the signals themselves, newest first, at most <see cref="ScanBacktest.MaxStoredSignals" />, and how far the venue's
///     reference pair moved over the period in percent (ADR 0071, section 5), empty when it is unknown.
/// </summary>
public sealed record ScanBacktestResult(
    int Pairs,
    IReadOnlyList<string> FailedPairs,
    ScanStudySummary Summary,
    IReadOnlyList<ScanSignal> Signals,
    bool SignalsTruncated,
    decimal? MarketChange = null);