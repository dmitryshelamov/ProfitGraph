using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Whether an attempt may be sent now, and at which price, or why it waits locally with the band bounds last seen.
/// </summary>
internal sealed record TInvestSendDecision(decimal? Price, OrderWaitingCode? WaitingCode, TInvestPriceBand? Band)
{
    public static TInvestSendDecision Send(decimal? price)
    {
        return new TInvestSendDecision(price, WaitingCode: null, Band: null);
    }

    public static TInvestSendDecision Wait(OrderWaitingCode code, TInvestPriceBand? band)
    {
        return new TInvestSendDecision(Price: null, code, band);
    }
}