using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Чистый выбор одного активного допустимого лимитного ордера. После каждого исполнения
///     и причинных реакций вызывается заново; уже пройденная часть пути не переигрывается.
///     В режиме <see cref="LimitFillMode.Through" /> цена должна пройти лимит строго (ADR 0057): касание не исполняет.
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
        bool through = account.Settings.LimitFill == LimitFillMode.Through;

        if (currentPrice < Math.Min(start, end) || currentPrice > Math.Max(start, end))
        {
            throw new ArgumentOutOfRangeException(nameof(currentPrice), "Current price must lie on the segment.");
        }

        OrderMatchCandidate? nearest = null;

        IReadOnlyList<Order> active = account.Orders.Active;

        for (var index = 0; index < active.Count; index++)
        {
            Order order = active[index];

            if (order.Type != OrderType.Limit || !order.IsEligibleAt(candleIndex) || !account.CanExecute(order))
            {
                continue;
            }

            decimal limit = order.LimitPrice!.Value;
            decimal pathPrice;

            if (IsExecutable(order.Side, currentPrice, limit, through))
            {
                pathPrice = currentPrice;
            }
            else if ((order.Side == OrderSide.Buy && !ascending && (through ? limit > end : limit >= end))
                     || (order.Side == OrderSide.Sell && ascending && (through ? limit < end : limit <= end)))
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

    /// <summary>
    ///     The earliest order executable at a reached point. Inside a segment, <paramref name="segmentEnd" /> is where the path
    ///     goes next, so an order whose limit is the point is executable in <see cref="LimitFillMode.Through" /> only when the
    ///     path continues beyond it; at a candle boundary it is null.
    /// </summary>
    public static OrderMatchCandidate? NextAtPrice(decimal price, TradingAccount account, int candleIndex, decimal? segmentEnd = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        ArgumentOutOfRangeException.ThrowIfNegative(candleIndex);
        bool through = account.Settings.LimitFill == LimitFillMode.Through;
        OrderMatchCandidate? earliest = null;

        IReadOnlyList<Order> active = account.Orders.Active;

        for (var index = 0; index < active.Count; index++)
        {
            Order order = active[index];

            if (order.Type == OrderType.Limit && order.IsEligibleAt(candleIndex) && account.CanExecute(order)
                && (IsExecutable(order.Side, price, order.LimitPrice!.Value, through) || TradesThrough(order.Side, price, order.LimitPrice!.Value, segmentEnd))
                && (earliest is null || order.Id < earliest.OrderId))
            {
                earliest = new OrderMatchCandidate(order.Id, price);
            }
        }

        return earliest;
    }

    private static bool IsExecutable(OrderSide side, decimal price, decimal limit, bool through)
    {
        if (through)
        {
            return side == OrderSide.Buy ? price < limit : price > limit;
        }

        return side == OrderSide.Buy ? price <= limit : price >= limit;
    }

    private static bool TradesThrough(OrderSide side, decimal price, decimal limit, decimal? segmentEnd)
    {
        return price == limit && segmentEnd is { } end && (side == OrderSide.Buy ? end < limit : end > limit);
    }
}