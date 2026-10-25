using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Closed history for a live engine, which still needs contiguous candles: live closures arrive with ADR 0023's TC3.
/// </summary>
internal static class LiveWarmupHistory
{
    public static IReadOnlyList<Candle> Contiguous(HistoricalCandlesInternal history)
    {
        return history.Closures.Count == 0
            ? history.Candles
            : throw new InvalidOperationException("Live bots cannot warm up across market closures yet.");
    }
}