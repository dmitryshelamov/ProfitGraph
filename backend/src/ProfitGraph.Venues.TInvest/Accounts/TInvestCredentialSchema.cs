using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.TInvest.Accounts;

internal sealed class TInvestCredentialSchema : IVenueCredentialSchema
{
    public VenueId Venue => TInvestVenue.VenueId;

    public IReadOnlyList<string> Fields { get; } = [TInvestCredentialFields.AccountId, TInvestCredentialFields.Token];
}