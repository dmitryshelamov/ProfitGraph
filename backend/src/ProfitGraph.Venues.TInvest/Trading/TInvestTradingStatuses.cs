using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using OrderType = ProfitGraph.Engine.Accounting.OrderType;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The share's current trading status (<c>MarketDataService/GetTradingStatus</c>), the only authority to send (ADR
///     0023): API trading and the order type's flag must both be available. A schedule never authorizes a submit.
/// </summary>
internal sealed class TInvestTradingStatuses(TInvestTransport transport)
{
    public async Task<bool> Allows(TInvestBotContext context, OrderType type, CancellationToken cancellationToken)
    {
        GetTradingStatusResponse status = await transport.Unary(
            context.Token,
            TInvestMethodGroup.MarketData,
            (invoker, options) => new MarketDataService.MarketDataServiceClient(invoker).GetTradingStatusAsync(
                new GetTradingStatusRequest { InstrumentId = context.Share.Uid },
                options),
            cancellationToken);

        return status.ApiTradeAvailableFlag && (type == OrderType.Limit ? status.LimitOrderAvailableFlag : status.MarketOrderAvailableFlag);
    }
}