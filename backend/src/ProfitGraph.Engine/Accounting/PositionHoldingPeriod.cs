using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Derives completed holding candles from an opening time and the explicit market interval.
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
        long elapsedTicks = (closedThrough - openedAt).Ticks;

        return elapsedTicks < 0 ? null : elapsedTicks / intervalTicks + (elapsedTicks % intervalTicks == 0 ? 0 : 1);
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