using MediatR;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.GetInstrumentRules;

internal sealed class GetInstrumentRulesQueryHandler(VenueRegistry venues, VenuePorts<IInstrumentRulesSource> sources, MarketDataCredentials credentials)
    : IRequestHandler<GetInstrumentRulesQuery, InstrumentRulesDefinitionInternal>
{
    public async Task<InstrumentRulesDefinitionInternal> Handle(GetInstrumentRulesQuery request, CancellationToken cancellationToken)
    {
        IVenueDescriptor venue = venues.Find(request.Venue)!;
        venue.TryParseSymbol(request.Symbol, out InstrumentSymbol? symbol);
        IInstrumentRulesSource source = sources.Find(venue.Id)!;
        using IDisposable access = MarketDataAccess.Use(await credentials.Read(request.UserId, request.CredentialId, venue.Id, cancellationToken));
        InstrumentRulesDefinitionInternal definition = await source.Get(symbol!, cancellationToken);

        return definition.Venue == venue.Id.Value && definition.Symbol == symbol!.Value
            ? definition
            : throw new InstrumentRulesException(InstrumentRulesErrorCodes.InvalidProviderData, "The venue returned rules of another instrument.");
    }
}