using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A Bybit credential is an API key and secret, entered in the web form. It is enrolled for Spot and also serves USDT
///     perpetuals (ADR 0061).
/// </summary>
internal sealed class BybitCredentialSchema : IVenueCredentialSchema
{
    public VenueId Venue => BybitMarket.Spot.Venue;

    public IReadOnlyList<string> Fields { get; } = [ExchangeCredentialFields.ApiKey, ExchangeCredentialFields.ApiSecret];

    public string PublicField => ExchangeCredentialFields.ApiKey;
}