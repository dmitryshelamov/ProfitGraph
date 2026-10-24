using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Converters;

public static class CandleInternalConverter
{
    public static CandleInternal ToInternal(this Candle candle)
    {
        return new CandleInternal(candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
    }
}