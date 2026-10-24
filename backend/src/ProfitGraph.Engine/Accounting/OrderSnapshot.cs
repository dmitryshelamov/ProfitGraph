namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Неизменяемое состояние ордера на момент события, включая исходный узел и резервы.
/// </summary>
public sealed record OrderSnapshot(
    long Id,
    string? Tag,
    OrderSide Side,
    OrderType Type,
    decimal? Quantity,
    decimal? LimitPrice,
    decimal? QuoteBudget,
    decimal Reservation,
    string OriginNodeId,
    int EligibleFromCandleIndex,
    OrderStatus Status)
{
    public static OrderSnapshot Capture(Order order)
    {
        return new OrderSnapshot(
            order.Id,
            order.Tag,
            order.Side,
            order.Type,
            order.Quantity,
            order.LimitPrice,
            order.QuoteBudget,
            order.Reservation,
            order.OriginNodeId,
            order.EligibleFromCandleIndex,
            order.Status);
    }
}