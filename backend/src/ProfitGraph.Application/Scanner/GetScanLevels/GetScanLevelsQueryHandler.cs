using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Application.Scanner.GetScanLevels;

internal sealed class GetScanLevelsQueryHandler(IEnumerable<IMarketScanSource> sources, ScanTimeframeLevelsReader levels)
    : IRequestHandler<GetScanLevelsQuery, ScanLevelsInternal>
{
    public Task<ScanLevelsInternal> Handle(GetScanLevelsQuery query, CancellationToken cancellationToken)
    {
        sources.Find(query.Venue);

        return levels.Read(query.Venue, query.Symbol, query.Timeframe, cancellationToken);
    }
}