using MediatR;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Converters;

namespace ProfitGraph.Application.MarketData.GetVenues;

internal sealed class GetVenuesQueryHandler(VenueRegistry venues) : IRequestHandler<GetVenuesQuery, IReadOnlyList<VenueInternal>>
{
    public Task<IReadOnlyList<VenueInternal>> Handle(GetVenuesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<VenueInternal> result = [.. venues.Venues.Select(venue => venue.ToInternal())];

        return Task.FromResult(result);
    }
}