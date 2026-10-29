namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A venue's current scanner snapshot; <see cref="ComputedAt" /> is null before the first one.
/// </summary>
public sealed record ScanSnapshotResponse(string Venue, DateTimeOffset? ComputedAt, IReadOnlyList<ScanSnapshotRowResponse> Rows);