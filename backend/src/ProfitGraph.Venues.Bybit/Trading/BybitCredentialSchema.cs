using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A Bybit credential is an API key and secret, entered in the web form.
/// </summary>
internal sealed class BybitCredentialSchema : IVenueCredentialSchema
{
    public VenueId Venue => BybitVenue.VenueId;

    public IReadOnlyList<string> Fields { get; } = [ExchangeCredentialFields.ApiKey, ExchangeCredentialFields.ApiSecret];
}