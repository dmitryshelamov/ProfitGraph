using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.History;

/// <summary>
///     An immutable, complete copy of the candles one run replays (Profit Fox Designer ADR 0021): the warm-up before
///     <see cref="From" /> and the replay range up to <see cref="To" />, contiguous on the timeframe grid. A later import
///     or download cannot change it.
/// </summary>
public sealed class PreparedHistory
{
    public CandleSeries Series { get; }
    public DateTimeOffset HistoryFrom { get; }
    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset LatestClosed { get; }
    public int WarmupCount { get; }
    public IReadOnlyList<Candle> Candles { get; }

    /// <summary>
    ///     SHA-256 of the series, the partition and every candle, as lowercase hex; equal identities mean equal input.
    /// </summary>
    public string Identity { get; }

    internal PreparedHistory(ResolvedCandleRange range, DateTimeOffset historyFrom, int warmupCount, Candle[] candles)
    {
        Series = range.Series;
        HistoryFrom = historyFrom;
        From = range.From;
        To = range.To;
        ObservedAt = range.ObservedAt;
        LatestClosed = range.LatestClosed;
        WarmupCount = warmupCount;

        // Preparation hands over this array exclusively; only its read-only view escapes.
        Candles = Array.AsReadOnly(candles);
        Identity = HistoryIdentityCalculator.Calculate(range, historyFrom, warmupCount, candles);
    }

    public PreparedReplay ToReplay()
    {
        return new PreparedReplay(Series.Timeframe, Candles.Take(WarmupCount), Candles.Skip(WarmupCount));
    }
}