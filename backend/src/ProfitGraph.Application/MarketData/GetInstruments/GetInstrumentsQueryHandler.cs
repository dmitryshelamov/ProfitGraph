using MediatR;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.MarketData.GetInstruments;

internal sealed class GetInstrumentsQueryHandler(VenueRegistry venues, VenuePorts<IInstrumentCatalog> catalogs)
    : IRequestHandler<GetInstrumentsQuery, IReadOnlyList<InstrumentInternal>>
{
    public Task<IReadOnlyList<InstrumentInternal>> Handle(GetInstrumentsQuery request, CancellationToken cancellationToken)
    {
        IInstrumentCatalog catalog = catalogs.Find(venues.Find(request.Venue)!.Id)!;

        return catalog.Search(request.Query.Trim(), request.Limit, cancellationToken);
    }
}