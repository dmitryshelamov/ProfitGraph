using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     A venue's history of one requested range: its closed candles and the ranges in which the market was closed
///     (ADR 0023, TC1). Every grid interval of the range is a candle or lies in a closure.
/// </summary>
public sealed record HistoricalCandlesInternal(IReadOnlyList<Candle> Candles, IReadOnlyList<CandleRangeInternal> Closures)
{
    /// <summary>
    ///     History of a market that never closes.
    /// </summary>
    public static HistoricalCandlesInternal Contiguous(IReadOnlyList<Candle> candles)
    {
        return new HistoricalCandlesInternal(candles, []);
    }
}