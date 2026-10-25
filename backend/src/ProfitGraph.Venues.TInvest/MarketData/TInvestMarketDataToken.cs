using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     The token of the caller's T-Invest credential that the use case opened as market data access (ADR 0023, TC6), or
///     null when the use case named none.
/// </summary>
internal static class TInvestMarketDataToken
{
    public static TInvestToken? Current()
    {
        ExchangeCredentialSecretsInternal? credential = MarketDataAccess.Current;

        return credential is not null && credential.Venue == TInvestVenue.VenueId.Value
            ? new TInvestToken(credential.Field(TInvestCredentialFields.Token))
            : null;
    }
}