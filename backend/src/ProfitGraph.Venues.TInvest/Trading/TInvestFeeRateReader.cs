using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The account's commission rate for a share (ADR 0023): T-Invest has no tariff-rate method, so a limit buy at the last
///     price worth at least <see cref="TInvestOptions.CommissionProbeNotional" /> is priced with <c>OrdersService/GetOrderPrice</c>,
///     which places nothing, and the rate is its deal commission over its order amount, to six decimal places. Limit and
///     market orders get the same rate. Without an answer the bot is not created.
/// </summary>
internal sealed class TInvestFeeRateReader(TInvestShares shares, TInvestPrices prices, TInvestTransport transport, IOptions<TInvestOptions> options)
    : IVenueFeeRateReader
{
    public VenueId Venue => TInvestVenue.VenueId;

    public async Task<VenueFeeRatesInternal> Read(ExchangeCredentialSecretsInternal credential, InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        var token = new TInvestToken(credential.Field(TInvestCredentialFields.Token));

        try
        {
            TInvestShare share = await shares.Get(token, Parse(symbol), cancellationToken);
            decimal price = await prices.Last(token, share, cancellationToken) ?? throw new VenueFeeRatesUnavailableException("T-Invest reports no price for the share.");
            var lots = (long)Math.Max(val1: 1m, Math.Ceiling(options.Value.CommissionProbeNotional / (price * share.Lot)));
            GetOrderPriceResponse response = await transport.Unary(
                token,
                TInvestMethodGroup.Orders,
                (invoker, callOptions) => new OrdersService.OrdersServiceClient(invoker).GetOrderPriceAsync(
                    new GetOrderPriceRequest
                    {
                        AccountId = credential.Field(TInvestCredentialFields.AccountId),
                        InstrumentId = share.Uid,
                        Price = price.ToQuotation(),
                        Direction = OrderDirection.Buy,
                        Quantity = lots
                    },
                    callOptions),
                cancellationToken);
            decimal amount = response.InitialOrderAmount?.ToDecimal() ?? 0m;
            decimal commission = response.DealCommission?.ToDecimal() ?? -1m;

            if (amount <= 0m || commission < 0m || commission >= amount)
            {
                throw new VenueFeeRatesUnavailableException("T-Invest did not price the commission of an order.");
            }

            decimal rate = Math.Round(commission / amount, decimals: 6, MidpointRounding.AwayFromZero);

            return new VenueFeeRatesInternal(rate, rate);
        }
        catch (TInvestRpcException)
        {
            throw new VenueFeeRatesUnavailableException();
        }
        catch (InstrumentRulesException)
        {
            throw new VenueFeeRatesUnavailableException("T-Invest has no such tradable share.");
        }
    }

    private static TInvestSymbol Parse(InstrumentSymbol symbol)
    {
        return TInvestSymbol.TryParse(symbol.Value, out TInvestSymbol? parsed)
            ? parsed
            : throw new VenueFeeRatesUnavailableException("The symbol is not a T-Invest share.");
    }
}