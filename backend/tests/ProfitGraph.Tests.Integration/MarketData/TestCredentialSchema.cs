using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     A test venue takes an API key and secret from the web form, like Bybit.
/// </summary>
public sealed class TestCredentialSchema(string venue) : IVenueCredentialSchema
{
    public VenueId Venue { get; } = new VenueId(venue);

    public IReadOnlyList<string> Fields { get; } = [ExchangeCredentialFields.ApiKey, ExchangeCredentialFields.ApiSecret];

    public string PublicField => ExchangeCredentialFields.ApiKey;
}