using ProfitGraph.Engine.Market;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     The Invest API interval of each supported timeframe and the longest window one <c>GetCandles</c> request may cover
///     (Profit Fox Designer ADR 0074); each window stays under the documented candle count.
/// </summary>
internal static class TInvestCandleWindows
{
    public static (CandleInterval Interval, TimeSpan Window) For(Timeframe timeframe)
    {
        return timeframe.Key switch
        {
            "1m" => (CandleInterval._1Min, TimeSpan.FromDays(1)),
            "3m" => (CandleInterval._3Min, TimeSpan.FromDays(1)),
            "5m" => (CandleInterval._5Min, TimeSpan.FromDays(7)),
            "15m" => (CandleInterval._15Min, TimeSpan.FromDays(21)),
            "30m" => (CandleInterval._30Min, TimeSpan.FromDays(21)),
            "1h" => (CandleInterval.Hour, TimeSpan.FromDays(90)),
            "2h" => (CandleInterval._2Hour, TimeSpan.FromDays(90)),
            "4h" => (CandleInterval._4Hour, TimeSpan.FromDays(90)),
            _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe.Key, "T-Invest has no such candle interval.")
        };
    }
}