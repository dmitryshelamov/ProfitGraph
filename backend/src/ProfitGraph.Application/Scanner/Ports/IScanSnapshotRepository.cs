using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.Ports;

/// <summary>
///     The stored current scanner snapshot, one row per venue and pair; no history is kept (ADR 0059).
/// </summary>
public interface IScanSnapshotRepository
{
    /// <summary>
    ///     Replaces the venue's snapshot in the active transaction and notifies <c>scan_snapshot</c> when it commits.
    /// </summary>
    Task Replace(ScanSnapshotInternal snapshot, CancellationToken cancellationToken);

    /// <summary>
    ///     The venue's stored snapshot, or null before the first one.
    /// </summary>
    Task<ScanSnapshotInternal?> Get(string venue, CancellationToken cancellationToken);
}