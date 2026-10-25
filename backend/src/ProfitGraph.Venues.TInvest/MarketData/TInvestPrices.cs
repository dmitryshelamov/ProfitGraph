using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     The last trade price of a share (<c>MarketDataService/GetLastPrices</c>), or null when T-Invest reports none.
/// </summary>
internal sealed class TInvestPrices(TInvestTransport transport)
{
    public async Task<decimal?> Last(TInvestToken token, TInvestShare share, CancellationToken cancellationToken)
    {
        GetLastPricesResponse response = await transport.Unary(
            token,
            TInvestMethodGroup.MarketData,
            (invoker, options) => new MarketDataService.MarketDataServiceClient(invoker).GetLastPricesAsync(
                new GetLastPricesRequest { InstrumentId = { share.Uid } },
                options),
            cancellationToken);
        var price = response.LastPrices.SingleOrDefault(last => last.InstrumentUid == share.Uid)?.Price?.ToDecimal();

        return price > 0m ? price : null;
    }
}