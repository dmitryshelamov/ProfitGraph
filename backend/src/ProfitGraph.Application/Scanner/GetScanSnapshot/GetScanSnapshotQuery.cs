using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanSnapshot;

/// <summary>
///     The venue's stored scanner snapshot; empty, without a computation time, before the first one.
/// </summary>
public sealed record GetScanSnapshotQuery(string Venue) : IQuery<ScanSnapshotInternal>;