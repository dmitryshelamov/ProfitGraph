using MediatR;

using ProfitGraph.Domain.Accounts.Snapshots;

namespace ProfitGraph.Application.Accounts.ThinAccountSnapshots;

internal sealed class ThinAccountSnapshotsCommandHandler(IAccountSnapshotRepository snapshots, AccountSnapshotOptions options, TimeProvider clock)
    : IRequestHandler<ThinAccountSnapshotsCommand, int>
{
    public Task<int> Handle(ThinAccountSnapshotsCommand command, CancellationToken cancellationToken)
    {
        return snapshots.ThinToHourly(clock.GetUtcNow() - options.FullDetail, cancellationToken);
    }
}