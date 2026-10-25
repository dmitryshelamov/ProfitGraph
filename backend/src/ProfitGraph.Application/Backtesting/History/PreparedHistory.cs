using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.History;

/// <summary>
///     An immutable, complete copy of the candles one run replays (Profit Fox Designer ADR 0021): the warm-up before
///     <see cref="From" /> and the replay range up to <see cref="To" />, contiguous on the timeframe grid except where
///     <see cref="Closures" /> cover a gap (ADR 0023, TC1). A later import or download cannot change it.
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
    ///     The merged market closures between <see cref="HistoryFrom" /> and <see cref="To" />; empty for a market that never
    ///     closes.
    /// </summary>
    public IReadOnlyList<MarketClosure> Closures { get; }

    /// <summary>
    ///     SHA-256 of the series, the partition, every candle and every closure, as lowercase hex; equal identities mean
    ///     equal input.
    /// </summary>
    public string Identity { get; }

    internal PreparedHistory(ResolvedCandleRange range, DateTimeOffset historyFrom, int warmupCount, Candle[] candles, IReadOnlyList<CandleRangeInternal> closures)
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
        Closures = Array.AsReadOnly(closures.Select(closure => new MarketClosure(closure.From, closure.To)).ToArray());
        Identity = HistoryIdentityCalculator.Calculate(range, historyFrom, warmupCount, candles, Closures);
    }

    public PreparedReplay ToReplay()
    {
        return new PreparedReplay(Series.Timeframe, Candles.Take(WarmupCount), Candles.Skip(WarmupCount), Closures);
    }
}