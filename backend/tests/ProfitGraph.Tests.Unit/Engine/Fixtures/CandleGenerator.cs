using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

public static class CandleGenerator
{
    public static Candle Generate(DateTimeOffset? openTime = null)
    {
        return new Candle(openTime ?? DateTimeOffset.UnixEpoch, Open: 100, High: 110, Low: 90, Close: 105, Volume: 1);
    }
}