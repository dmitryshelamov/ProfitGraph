using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Closed history for a live engine (ADR 0023, TC3): at least the needed candles up to a given candle, reaching further
///     back over market closures when they leave it short, at most 16 times and each time doubling the span. A venue
///     without closures is read once, exactly as before.
/// </summary>
internal static class LiveHistory
{
    private const int MaxExtensions = 16;

    public static async Task<HistoricalCandlesInternal> Read(
        IHistoricalCandleSource source,
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset through,
        DateTimeOffset to,
        int needed,
        CancellationToken cancellationToken)
    {
        long span = Math.Max(needed, val2: 1);
        DateTimeOffset end = through + timeframe.Duration;

        for (var extension = 0;; extension++)
        {
            DateTimeOffset from = end - timeframe.Duration * span;
            HistoricalCandlesInternal history = await source.Get(symbol, timeframe, from, to, cancellationToken);

            if (history.Closures.Count == 0 || history.Candles.Count(candle => candle.OpenTime <= through) >= needed || extension == MaxExtensions)
            {
                return history;
            }

            span *= 2;
        }
    }

    public static IReadOnlyList<MarketClosure> Closures(HistoricalCandlesInternal history)
    {
        return [.. history.Closures.Select(closure => new MarketClosure(closure.From, closure.To))];
    }
}