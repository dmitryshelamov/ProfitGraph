using MediatR;

using ProfitGraph.Application.Scanner.Alerts;
using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Application.Scanner.ReplaceScanSnapshot;

/// <summary>
///     Stores the venue's new snapshot and, in the same transaction, queues the alerts of the presets it changes (ADR 0059).
/// </summary>
internal sealed class ReplaceScanSnapshotCommandHandler(IScanSnapshotRepository snapshots, ScanAlertQueue alerts) : IRequestHandler<ReplaceScanSnapshotCommand>
{
    public async Task Handle(ReplaceScanSnapshotCommand command, CancellationToken cancellationToken)
    {
        await snapshots.Replace(command.Snapshot, cancellationToken);
        await alerts.Check(command.Snapshot, cancellationToken);
    }
}