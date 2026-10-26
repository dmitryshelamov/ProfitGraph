using FluentValidation;

using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.MarketData.GetInstrumentRules;

internal sealed class GetInstrumentRulesQueryValidator : AbstractValidator<GetInstrumentRulesQuery>
{
    public GetInstrumentRulesQueryValidator(VenueRegistry venues, VenuePorts<IInstrumentRulesSource> sources)
    {
        RuleFor(query => query.Venue)
            .Must(venue => venues.Find(venue) is not null)
            .WithErrorCode("unknown_venue")
            .WithMessage("The venue is not registered.")
            .DependentRules(() =>
            {
                RuleFor(query => query.Venue)
                    .Must(venue => sources.Find(venues.Find(venue)!.Id) is not null)
                    .WithErrorCode(InstrumentRulesErrorCodes.InvalidRequest)
                    .WithMessage("This venue offers no instrument rules.");
                RuleFor(query => query.Symbol)
                    .Must((query, symbol) => venues.Find(query.Venue)!.TryParseSymbol(symbol, out _))
                    .WithErrorCode("invalid_symbol")
                    .WithMessage("The symbol is not a valid symbol of the venue.");
            });
    }
}