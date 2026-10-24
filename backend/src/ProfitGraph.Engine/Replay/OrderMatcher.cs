using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Чистый выбор одного активного допустимого лимитного ордера. После каждого исполнения
///     и причинных реакций вызывается заново; уже пройденная часть пути не переигрывается.
/// </summary>
public static class OrderMatcher
{
    public static OrderMatchCandidate? Next(PriceSegment segment, decimal currentPrice, TradingAccount account, int candleIndex)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return Next(segment.Start, segment.End, currentPrice, account, candleIndex);
    }

    internal static OrderMatchCandidate? Next(decimal start, decimal end, decimal currentPrice, TradingAccount account, int candleIndex)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentOutOfRangeException.ThrowIfNegative(candleIndex);
        bool ascending = end > start;

        if (currentPrice < Math.Min(start, end) || currentPrice > Math.Max(start, end))
        {
            throw new ArgumentOutOfRangeException(nameof(currentPrice), "Current price must lie on the segment.");
        }

        OrderMatchCandidate? nearest = null;

        IReadOnlyList<Order> active = account.Orders.Active;

        for (var index = 0; index < active.Count; index++)
        {
            Order order = active[index];

            if (order.Type != OrderType.Limit || !order.IsEligibleAt(candleIndex))
            {
                continue;
            }

            decimal limit = order.LimitPrice!.Value;
            decimal pathPrice;

            if (IsExecutable(order.Side, currentPrice, limit))
            {
                pathPrice = currentPrice;
            }
            else if ((order.Side == OrderSide.Buy && !ascending && limit >= end)
                     || (order.Side == OrderSide.Sell && ascending && limit <= end))
            {
                pathPrice = limit;
            }
            else
            {
                continue;
            }

            if (nearest is null || (ascending ? pathPrice < nearest.PathPrice : pathPrice > nearest.PathPrice)
                || (pathPrice == nearest.PathPrice && order.Id < nearest.OrderId))
            {
                nearest = new OrderMatchCandidate(order.Id, pathPrice);
            }
        }

        return nearest;
    }

    public static OrderMatchCandidate? NextAtPrice(decimal price, TradingAccount account, int candleIndex)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        ArgumentOutOfRangeException.ThrowIfNegative(candleIndex);
        OrderMatchCandidate? earliest = null;

        IReadOnlyList<Order> active = account.Orders.Active;

        for (var index = 0; index < active.Count; index++)
        {
            Order order = active[index];

            if (order.Type == OrderType.Limit && order.IsEligibleAt(candleIndex)
                && IsExecutable(order.Side, price, order.LimitPrice!.Value)
                && (earliest is null || order.Id < earliest.OrderId))
            {
                earliest = new OrderMatchCandidate(order.Id, price);
            }
        }

        return earliest;
    }

    private static bool IsExecutable(OrderSide side, decimal price, decimal limit)
    {
        return side == OrderSide.Buy ? price <= limit : price >= limit;
    }
}