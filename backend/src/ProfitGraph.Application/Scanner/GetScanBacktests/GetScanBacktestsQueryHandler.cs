using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Application.Scanner.GetScanBacktests;

internal sealed class GetScanBacktestsQueryHandler(IEnumerable<IMarketScanSource> sources, IScanBacktestReader reader)
    : IRequestHandler<GetScanBacktestsQuery, IReadOnlyList<ScanBacktestListItemInternal>>
{
    private const int Limit = 10;

    public Task<IReadOnlyList<ScanBacktestListItemInternal>> Handle(GetScanBacktestsQuery query, CancellationToken cancellationToken)
    {
        return reader.List(query.UserId, sources.Find(query.Venue).Venue.Value, Limit, cancellationToken);
    }
}