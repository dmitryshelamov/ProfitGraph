using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Accounts;

/// <summary>
///     The brokerage accounts a token can see (<c>UsersService/GetAccounts</c>). A bot may trade only through an open
///     brokerage or IIS account with full access (Profit Fox Designer ADR 0074); every other account is listed as ineligible
///     with its reason. A refused token is a validation error; any other failure is unavailability.
/// </summary>
internal sealed class TInvestAccountDirectory(TInvestTransport transport) : IVenueAccountDirectory
{
    public VenueId Venue => TInvestVenue.VenueId;

    public string AccountField => TInvestCredentialFields.AccountId;

    public async Task<IReadOnlyList<VenueAccountInternal>> List(IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var token = new TInvestToken(fields[TInvestCredentialFields.Token]);
        GetAccountsResponse response;

        try
        {
            response = await transport.Unary(
                token,
                TInvestMethodGroup.Users,
                (invoker, options) => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest(), options),
                cancellationToken);
        }
        catch (TInvestRpcException error) when (error.Kind is TInvestFailureKind.Unauthenticated or TInvestFailureKind.PermissionDenied)
        {
            throw new ApplicationValidationException(
                [new InputError("invalid_venue_credential", $"Fields.{TInvestCredentialFields.Token}", "T-Invest refused the token.")]);
        }
        catch (TInvestRpcException)
        {
            throw new VenueAccountsUnavailableException();
        }

        return [.. response.Accounts.Where(account => !string.IsNullOrEmpty(account.Id)).Select(ToInternal)];
    }

    /// <summary>
    ///     The account's kind by its type: a brokerage account, an IIS or any other.
    /// </summary>
    internal static string Kind(AccountType type)
    {
        return type switch
        {
            AccountType.Tinkoff => "broker",
            AccountType.TinkoffIis => "iis",
            _ => "other"
        };
    }

    private static VenueAccountInternal ToInternal(Account account)
    {
        string kind = Kind(account.Type);
        string? ineligibility = (account.Status, account.AccessLevel, kind) switch
        {
            (not AccountStatus.Open, _, _) => "not_open",
            (_, not AccessLevel.AccountAccessLevelFullAccess, _) => "not_full_access",
            (_, _, "other") => "unsupported_kind",
            _ => null
        };

        return new VenueAccountInternal(account.Id, account.Name, kind, ineligibility is null, ineligibility);
    }
}