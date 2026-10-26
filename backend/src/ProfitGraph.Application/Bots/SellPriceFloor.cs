using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     The lowest limit price a sell may have (ADR 0018, ADR 0024). A sell is not bounded by the per-order limit, so a mistyped
///     price is the one way it can give the position away: a limit sell below half of the last closed candle's close is
///     refused. A market sell has no price of its own and is not floored.
/// </summary>
internal static class SellPriceFloor
{
    internal const decimal Ratio = 0.5m;

    public static bool IsBelow(OrderSnapshot order, decimal lastClose)
    {
        return order.Side == OrderSide.Sell && order.LimitPrice is { } limit && limit < lastClose * Ratio;
    }
}