using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Sizes an engine order in whole lots of the share (ADR 0023): a limit or a market sell from its quantity, which must be
///     a whole number of lots as the share's rules require, and a market buy from its quote budget at the last price plus
///     the configured slippage, rounded down to whole lots. A limit price must lie on the tick. Anything else is null.
/// </summary>
internal static class TInvestOrderPlans
{
    public static TInvestOrderPlan? For(OrderSnapshot order, TInvestShare share, decimal? lastPrice, decimal marketBuySlippage)
    {
        if (order.Type == OrderType.Limit)
        {
            if (order.Quantity is not { } quantity || order.LimitPrice is not { } limit)
            {
                return null;
            }

            return limit > 0m && limit % share.PriceIncrement == 0m && WholeLots(quantity, share) is { } lots ? new TInvestOrderPlan(lots, limit) : null;
        }

        if (order.Side == OrderSide.Buy && order.QuoteBudget is > 0m)
        {
            return lastPrice is > 0m && Lots(order.QuoteBudget.Value / (lastPrice.Value * (1m + marketBuySlippage)), share) is > 0 and var lots
                ? new TInvestOrderPlan(lots, Price: null)
                : null;
        }

        return order.Quantity is { } sold && WholeLots(sold, share) is { } soldLots ? new TInvestOrderPlan(soldLots, Price: null) : null;
    }

    private static long? WholeLots(decimal shares, TInvestShare share)
    {
        return shares > 0m && shares % share.Lot == 0m ? Lots(shares, share) : null;
    }

    private static long Lots(decimal shares, TInvestShare share)
    {
        return decimal.ToInt64(decimal.Floor(shares / share.Lot));
    }
}