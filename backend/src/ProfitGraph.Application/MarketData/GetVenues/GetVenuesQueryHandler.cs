using MediatR;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Converters;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.MarketData.GetVenues;

internal sealed class GetVenuesQueryHandler(VenueRegistry venues, VenuePorts<IVenueExecutionDefaults> defaults)
    : IRequestHandler<GetVenuesQuery, IReadOnlyList<VenueInternal>>
{
    public Task<IReadOnlyList<VenueInternal>> Handle(GetVenuesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<VenueInternal> result = [.. venues.Venues.Select(venue => venue.ToInternal(defaults.Find(venue.Id)?.Defaults))];

        return Task.FromResult(result);
    }
}