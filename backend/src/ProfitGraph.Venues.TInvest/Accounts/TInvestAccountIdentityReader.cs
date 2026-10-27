using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Accounts;

/// <summary>
///     Describes the credential's brokerage account from <c>UsersService/GetAccounts</c> (ADR 0049): its ID, the name the
///     owner gave it, its kind and the token's access level to it. An account the token no longer sees is a failure, so the
///     last description stays. Failures are reported by kind only.
/// </summary>
internal sealed class TInvestAccountIdentityReader(TInvestTransport transport) : IVenueAccountIdentityReader
{
    public VenueId Venue => TInvestVenue.VenueId;

    public async Task<AccountIdentityReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken)
    {
        GetAccountsResponse response;

        try
        {
            response = await transport.Unary(
                new TInvestToken(credential.Field(TInvestCredentialFields.Token)),
                TInvestMethodGroup.Users,
                (invoker, options) => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest(), options),
                cancellationToken);
        }
        catch (TInvestRpcException error)
        {
            return AccountIdentityReadInternal.Failed("venue_error", error.Kind.ToString());
        }

        string accountId = credential.Field(TInvestCredentialFields.AccountId);

        if (response.Accounts.FirstOrDefault(account => string.Equals(account.Id, accountId, StringComparison.Ordinal)) is not { } found)
        {
            return AccountIdentityReadInternal.Failed("account_not_found");
        }

        return AccountIdentityReadInternal.Observed(
            new AccountIdentityInternal(
                found.Id,
                found.Name,
                TInvestAccountDirectory.Kind(found.Type),
                found.AccessLevel switch
                {
                    AccessLevel.AccountAccessLevelFullAccess => "full_access",
                    AccessLevel.AccountAccessLevelReadOnly => "read_only",
                    AccessLevel.AccountAccessLevelNoAccess => "no_access",
                    _ => null
                },
                found.AccessLevel switch
                {
                    AccessLevel.AccountAccessLevelFullAccess => false,
                    AccessLevel.AccountAccessLevelReadOnly => true,
                    _ => null
                },
                [],
                ExpiresAt: null));
    }
}