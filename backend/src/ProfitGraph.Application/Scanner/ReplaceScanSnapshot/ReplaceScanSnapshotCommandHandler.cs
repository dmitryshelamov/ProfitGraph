using MediatR;

using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Application.Scanner.ReplaceScanSnapshot;

internal sealed class ReplaceScanSnapshotCommandHandler(IScanSnapshotRepository snapshots) : IRequestHandler<ReplaceScanSnapshotCommand>
{
    public Task Handle(ReplaceScanSnapshotCommand command, CancellationToken cancellationToken)
    {
        return snapshots.Replace(command.Snapshot, cancellationToken);
    }
}