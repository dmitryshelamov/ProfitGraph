using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.ReplaceScanSnapshot;

/// <summary>
///     The scanner computed a venue's metrics after a minute closed; the stored snapshot becomes this one.
/// </summary>
public sealed record ReplaceScanSnapshotCommand(ScanSnapshotInternal Snapshot) : ICommand;