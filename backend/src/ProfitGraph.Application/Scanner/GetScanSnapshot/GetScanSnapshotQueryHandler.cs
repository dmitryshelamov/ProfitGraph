using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Application.Scanner.GetScanSnapshot;

internal sealed class GetScanSnapshotQueryHandler(IEnumerable<IMarketScanSource> sources, IScanSnapshotRepository snapshots)
    : IRequestHandler<GetScanSnapshotQuery, ScanSnapshotInternal>
{
    public async Task<ScanSnapshotInternal> Handle(GetScanSnapshotQuery query, CancellationToken cancellationToken)
    {
        string venue = sources.Find(query.Venue).Venue.Value;

        return await snapshots.Get(venue, cancellationToken) ?? new ScanSnapshotInternal(venue, DateTimeOffset.MinValue, []);
    }
}