using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     The bot's per-order limit (ADR 0018). It bounds what a buy may spend, so a runaway strategy cannot commit more at
///     once. A sell is not valued against it: the engine rejects a sell above the available base before an intent exists
///     and reserves the quantity, so the position it closes was bought under the limit and a sell can only close it.
/// </summary>
internal static class PerOrderLimit
{
    public static bool Exceeds(OrderSnapshot order, decimal maxOrderQuote)
    {
        return order.Side == OrderSide.Buy && Notional(order) > maxOrderQuote;
    }

    public static decimal Notional(OrderSnapshot order)
    {
        return order.QuoteBudget ?? order.Quantity.GetValueOrDefault() * order.LimitPrice.GetValueOrDefault();
    }
}