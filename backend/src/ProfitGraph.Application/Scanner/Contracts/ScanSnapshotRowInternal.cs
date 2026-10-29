using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     One pair of a scanner snapshot: its metrics and whether its candles still have a gap the scanner has not filled.
/// </summary>
public sealed record ScanSnapshotRowInternal(string Symbol, ScanMetrics Metrics, bool Incomplete);