using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Accounts;

/// <summary>
///     The value of the credential's brokerage account in roubles (<c>OperationsService/GetPortfolio</c>, ADR 0023): the
///     portfolio total and each position by ticker. The available value is left unknown rather than guessed from cash that
///     may be blocked. Failures are reported by kind and status only.
/// </summary>
internal sealed class TInvestAccountBalanceReader(TInvestTransport transport, TimeProvider clock) : IAccountBalanceReader
{
    public VenueId Venue => TInvestVenue.VenueId;

    public async Task<AccountBalanceReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken)
    {
        PortfolioResponse portfolio;

        try
        {
            portfolio = await transport.Unary(
                new TInvestToken(credential.Field(TInvestCredentialFields.Token)),
                TInvestMethodGroup.Operations,
                (invoker, options) => new OperationsService.OperationsServiceClient(invoker).GetPortfolioAsync(
                    new PortfolioRequest { AccountId = credential.Field(TInvestCredentialFields.AccountId), Currency = PortfolioRequest.Types.CurrencyRequest.Rub },
                    options),
                cancellationToken);
        }
        catch (TInvestRpcException error)
        {
            return AccountBalanceReadInternal.Failed("venue_error", error.Kind.ToString());
        }

        if (portfolio.TotalAmountPortfolio is not { } total || !string.Equals(total.Currency, "rub", StringComparison.OrdinalIgnoreCase))
        {
            return AccountBalanceReadInternal.Failed("invalid_response", "portfolio total");
        }

        AccountCoinBalanceInternal[] positions =
        [
            .. portfolio.Positions
                .Where(position => !string.IsNullOrEmpty(position.Ticker) && position.Quantity is not null)
                .Select(position => new AccountCoinBalanceInternal(position.Ticker, position.Quantity.ToDecimal(), UsdValue: null))
        ];

        return AccountBalanceReadInternal.Observed(new AccountBalanceObservationInternal(total.ToDecimal(), "RUB", AvailableValue: null, positions, clock.GetUtcNow()));
    }
}