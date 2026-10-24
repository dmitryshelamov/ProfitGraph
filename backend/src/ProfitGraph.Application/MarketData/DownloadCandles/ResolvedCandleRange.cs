using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.DownloadCandles;

/// <summary>
///     A range of closed candles fixed against one clock reading: [From, To) on the series grid, with To at or before the
///     latest closed candle at <see cref="ObservedAt" />.
/// </summary>
public sealed record ResolvedCandleRange(CandleSeries Series, DateTimeOffset From, DateTimeOffset To, DateTimeOffset ObservedAt, DateTimeOffset LatestClosed)
{
    public long Count => (To.UtcTicks - From.UtcTicks) / Series.Timeframe.Duration.Ticks;
}