using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Derives completed holding candles from an opening time and the explicit market interval. Time in market closures
///     does not count (ADR 0023), so a night is not a candle held; without closures this is the elapsed grid time.
/// </summary>
internal static class PositionHoldingPeriod
{
    internal static decimal? BarsHeld(DispatchEvaluationContext context)
    {
        if (context.PositionOpenedAt is not { } openedAt || IsFlat(context.Account.BaseTotal, context.Rules, context.Settings, context.Market.MarketPrice))
        {
            return null;
        }

        ReachedMarketContext market = context.Market;
        DateTimeOffset closedThrough = market.Phase is PricePhase.Close or PricePhase.External ? market.CandleCloseTime : market.Candle.OpenTime;
        long intervalTicks = (market.CandleCloseTime - market.Candle.OpenTime).Ticks;
        long elapsedTicks = (closedThrough - openedAt).Ticks - ClosedTicks(context.Closures, openedAt, closedThrough);

        return elapsedTicks < 0 ? null : elapsedTicks / intervalTicks + (elapsedTicks % intervalTicks == 0 ? 0 : 1);
    }

    private static long ClosedTicks(IReadOnlyList<MarketClosure>? closures, DateTimeOffset from, DateTimeOffset to)
    {
        long closed = 0;

        if (closures is null || from >= to)
        {
            return closed;
        }

        foreach (MarketClosure closure in closures)
        {
            DateTimeOffset start = closure.From > from ? closure.From : from;
            DateTimeOffset end = closure.To < to ? closure.To : to;

            if (start < end)
            {
                closed += (end - start).Ticks;
            }
        }

        return closed;
    }

    internal static bool IsFlat(decimal quantity, InstrumentRules rules, ExecutionSettings settings, decimal referencePrice)
    {
        if (quantity == 0m)
        {
            return true;
        }

        decimal normalized = OrderNormalizer.NormalizeQuantity(quantity, rules);
        decimal sellPrice = SlippageModel.Apply(referencePrice, OrderSide.Sell, settings.Slippage);
        RejectionReason? reason = OrderNormalizer.CheckMinimums(normalized, sellPrice, rules);

        return reason is RejectionReason.ZeroQuantity or RejectionReason.BelowMinQuantity or RejectionReason.BelowMinNotional;
    }
}