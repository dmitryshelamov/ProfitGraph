using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Reads of the bot's account orders (<c>OrdersService</c>): one order by its request ID, which T-Invest answers with
///     <c>NotFound</c> when it does not know it, and the account's active orders.
/// </summary>
internal sealed class TInvestOrderReads(TInvestTransport transport)
{
    /// <summary>
    ///     The order T-Invest knows under the request ID, or null when it reports none. An empty answer is not proof that a
    ///     place request never arrived.
    /// </summary>
    public async Task<OrderState?> ByRequestId(TInvestBotContext context, Guid requestId, CancellationToken cancellationToken)
    {
        try
        {
            return await transport.Unary(
                context.Token,
                TInvestMethodGroup.Orders,
                (invoker, options) => new OrdersService.OrdersServiceClient(invoker).GetOrderStateAsync(
                    new GetOrderStateRequest
                    {
                        AccountId = context.AccountId,
                        OrderId = requestId.ToString("D"),
                        OrderIdType = OrderIdType.Request,
                        PriceType = PriceType.Currency
                    },
                    options),
                cancellationToken);
        }
        catch (TInvestRpcException error) when (error.Kind == TInvestFailureKind.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<OrderState>> Active(TInvestBotContext context, CancellationToken cancellationToken)
    {
        GetOrdersResponse response = await transport.Unary(
            context.Token,
            TInvestMethodGroup.Orders,
            (invoker, options) => new OrdersService.OrdersServiceClient(invoker).GetOrdersAsync(new GetOrdersRequest { AccountId = context.AccountId }, options),
            cancellationToken);

        return response.Orders;
    }
}