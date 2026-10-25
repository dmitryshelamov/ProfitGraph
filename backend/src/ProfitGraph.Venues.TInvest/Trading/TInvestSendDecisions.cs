using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The price band policy of a limit order (Profit Fox Designer ADR 0072, ADR 0023) with band <c>D..U</c> and price
///     <c>P</c>: a buy below <c>D</c> or a sell above <c>U</c> waits; a buy above <c>U</c> is sent at <c>U</c> rounded down
///     to the tick and a sell below <c>D</c> at <c>D</c> rounded up, never worse for the bot and never outside the band, or it
///     waits; a price inside the band, bounds included, is sent as it is. A missing band waits. A market order is not
///     checked against the band.
/// </summary>
internal static class TInvestSendDecisions
{
    public static TInvestSendDecision ForLimit(OrderSide side, decimal price, TInvestPriceBand? band, decimal tick)
    {
        if (band is null)
        {
            return TInvestSendDecision.Wait(OrderWaitingCode.PriceBand, band: null);
        }

        if (price >= band.Low && price <= band.High)
        {
            return TInvestSendDecision.Send(price);
        }

        if (side == OrderSide.Buy && price > band.High)
        {
            decimal bound = decimal.Floor(band.High / tick) * tick;

            return bound >= band.Low ? TInvestSendDecision.Send(bound) : TInvestSendDecision.Wait(OrderWaitingCode.PriceBand, band);
        }

        if (side == OrderSide.Sell && price < band.Low)
        {
            decimal bound = decimal.Ceiling(band.Low / tick) * tick;

            return bound <= band.High ? TInvestSendDecision.Send(bound) : TInvestSendDecision.Wait(OrderWaitingCode.PriceBand, band);
        }

        return TInvestSendDecision.Wait(OrderWaitingCode.PriceBand, band);
    }
}