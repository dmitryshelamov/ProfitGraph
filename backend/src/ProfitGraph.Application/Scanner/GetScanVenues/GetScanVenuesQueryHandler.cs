using MediatR;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Application.Scanner.GetScanVenues;

internal sealed class GetScanVenuesQueryHandler(IEnumerable<IMarketScanSource> sources, VenueRegistry venues)
    : IRequestHandler<GetScanVenuesQuery, IReadOnlyList<ScanVenueInternal>>
{
    public Task<IReadOnlyList<ScanVenueInternal>> Handle(GetScanVenuesQuery query, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ScanVenueInternal>>(
        [
            .. sources.Select(source => new ScanVenueInternal(
                source.Venue.Value,
                venues.Find(source.Venue.Value)?.Name ?? source.Venue.Value,
                source.QuoteAsset,
                venues.Find(source.Venue.Value)?.SupportsShort ?? false))
        ]);
    }
}