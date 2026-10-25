using ProfitGraph.Engine.Market;
using ProfitGraph.Infrastructure.MarketData.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.MarketData.Repositories.Converters;

internal static class CandleDbConverter
{
    public static Candle ToDomain(this CandleDb row)
    {
        return new Candle(row.OpenTime.ToUniversalTime(), row.Open, row.High, row.Low, row.Close, row.Volume) { NoTrades = row.NoTrades };
    }
}