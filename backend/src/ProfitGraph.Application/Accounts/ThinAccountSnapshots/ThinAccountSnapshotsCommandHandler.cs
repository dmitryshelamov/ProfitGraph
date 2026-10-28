using MediatR;

using ProfitGraph.Domain.Accounts.Snapshots;

namespace ProfitGraph.Application.Accounts.ThinAccountSnapshots;

internal sealed class ThinAccountSnapshotsCommandHandler(IAccountSnapshotRepository snapshots, TimeProvider clock) : IRequestHandler<ThinAccountSnapshotsCommand, int>
{
    private static readonly TimeSpan FullDetail = TimeSpan.FromDays(7);

    public Task<int> Handle(ThinAccountSnapshotsCommand command, CancellationToken cancellationToken)
    {
        return snapshots.ThinToHourly(clock.GetUtcNow() - FullDetail, cancellationToken);
    }
}