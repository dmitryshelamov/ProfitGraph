using MediatR;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.GetInstruments;

internal sealed class GetInstrumentsQueryHandler(VenueRegistry venues, VenuePorts<IInstrumentCatalog> catalogs, MarketDataCredentials credentials)
    : IRequestHandler<GetInstrumentsQuery, IReadOnlyList<InstrumentInternal>>
{
    public async Task<IReadOnlyList<InstrumentInternal>> Handle(GetInstrumentsQuery request, CancellationToken cancellationToken)
    {
        VenueId venue = venues.Find(request.Venue)!.Id;
        IInstrumentCatalog catalog = catalogs.Find(venue)!;
        using IDisposable access = MarketDataAccess.Use(await credentials.Read(request.UserId, request.CredentialId, venue, cancellationToken));

        return await catalog.Search(request.Query.Trim(), request.Limit, cancellationToken);
    }
}