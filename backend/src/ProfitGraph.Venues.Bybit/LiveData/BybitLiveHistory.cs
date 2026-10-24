using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     Fills a live gap from Bybit's public REST history and proves the fill is contiguous.
/// </summary>
internal sealed class BybitLiveHistory(Func<InstrumentSymbol, Timeframe, DateTimeOffset, DateTimeOffset, CancellationToken, Task<IReadOnlyList<Candle>>> read)
{
    /// <summary>
    ///     The closed candles after <paramref name="last" /> and before <paramref name="to" />, without a gap.
    /// </summary>
    public async Task<IReadOnlyList<Candle>> After(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset last,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        DateTimeOffset expected = last + timeframe.Duration;

        if (expected >= to)
        {
            return [];
        }

        IReadOnlyList<Candle> candles = await read(symbol, timeframe, expected, to, cancellationToken);
        var ordered = new List<Candle>();

        foreach (Candle candle in candles.OrderBy(candle => candle.OpenTime))
        {
            if (candle.OpenTime != expected)
            {
                throw new InvalidDataException("Public candle history has a gap.");
            }

            ordered.Add(candle);
            expected += timeframe.Duration;
        }

        return expected == to ? ordered : throw new InvalidDataException("Public candle history is incomplete.");
    }
}