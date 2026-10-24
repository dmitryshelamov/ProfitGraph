using FluentValidation;

using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.MarketData.GetInstruments;

internal sealed class GetInstrumentsQueryValidator : AbstractValidator<GetInstrumentsQuery>
{
    public GetInstrumentsQueryValidator(VenueRegistry venues, VenuePorts<IInstrumentCatalog> catalogs)
    {
        RuleFor(query => query.Venue)
            .Must(venue => venues.Find(venue) is not null)
            .WithErrorCode("unknown_venue")
            .WithMessage("The venue is not registered.")
            .DependentRules(() =>
            {
                RuleFor(query => query.Venue)
                    .Must(venue => catalogs.Find(venues.Find(venue)!.Id) is not null)
                    .WithErrorCode(InstrumentRulesErrorCodes.InvalidRequest)
                    .WithMessage("This venue offers no instrument list.");
            });
        RuleFor(query => query.Query).NotNull().MaximumLength(32);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, to: 100);
    }
}