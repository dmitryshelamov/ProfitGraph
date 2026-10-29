namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The current scanner snapshot of one venue (ADR 0059): every shown pair's metrics, computed at one moment.
/// </summary>
public sealed record ScanSnapshotInternal(string Venue, DateTimeOffset ComputedAt, IReadOnlyList<ScanSnapshotRowInternal> Rows);