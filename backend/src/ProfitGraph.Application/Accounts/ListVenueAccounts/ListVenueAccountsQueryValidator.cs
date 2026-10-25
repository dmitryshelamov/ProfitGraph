using FluentValidation;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Application.Accounts.ListVenueAccounts;

internal sealed class ListVenueAccountsQueryValidator : AbstractValidator<ListVenueAccountsQuery>
{
    public ListVenueAccountsQueryValidator(VenueRegistry venues, VenuePorts<IVenueCredentialSchema> schemas, VenuePorts<IVenueAccountDirectory> directories)
    {
        RuleFor(query => query.Venue)
            .Must(venue => venues.Find(venue) is not null)
            .WithErrorCode("unknown_venue")
            .WithMessage("The venue is not registered.")
            .DependentRules(() =>
            {
                RuleFor(query => query.Venue)
                    .Must(venue => CredentialFieldRules.Declared(venue, venues, schemas, directories) is not null)
                    .WithErrorCode(ExchangeCredentialErrorCodes.AccountsUnsupported)
                    .WithMessage("The venue's credentials do not select an account.")
                    .DependentRules(() => RuleFor(query => query.Fields)
                        .Must((query, fields) =>
                        {
                            string account = directories.Find(venues.Find(query.Venue)!.Id)!.AccountField;

                            return CredentialFieldRules.Matches(
                                fields,
                                CredentialFieldRules.Declared(query.Venue, venues, schemas, directories)!.Where(field => field != account));
                        })
                        .WithErrorCode("credential_fields_mismatch")
                        .WithMessage(
                            $"Give every field of the venue's credentials but the account, each with 1 to {CredentialFieldRules.MaxValueLength} characters without surrounding spaces."));
            });
    }
}