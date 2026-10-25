using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.MarketData;

namespace ProfitGraph.Venues.Steam.Trading;

/// <summary>
///     A Steam account's value is its wallet balance, in the wallet currency (ADR 0022): Steam converts nothing and neither does
///     ProfitGraph. Items in the inventory are not valued. A failure reports a stable reason, never provider text.
/// </summary>
internal sealed class SteamAccountBalanceReader(SteamMarketDataAccount account, TimeProvider clock) : IAccountBalanceReader
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<AccountBalanceReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken)
    {
        try
        {
            SteamWallet wallet = await account.WalletOf(credential.CredentialId, cancellationToken);

            return AccountBalanceReadInternal.Observed(
                new AccountBalanceObservationInternal(
                    wallet.Balance,
                    wallet.Currency.Code,
                    wallet.Balance,
                    [new AccountCoinBalanceInternal(wallet.Currency.Code, wallet.Balance, UsdValue: null)],
                    clock.GetUtcNow()));
        }
        catch (SteamMarketDataException exception)
        {
            return AccountBalanceReadInternal.Failed(exception.Unavailable ? "venue_unavailable" : "unsupported_response");
        }
    }
}