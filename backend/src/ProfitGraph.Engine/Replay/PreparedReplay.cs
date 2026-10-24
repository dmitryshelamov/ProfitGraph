using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Immutable history validated once and safely shared by independent replay trials.
/// </summary>
public sealed class PreparedReplay
{
    private readonly IReadOnlyList<Candle> _history;
    private readonly int _replayStart;

    public Timeframe Timeframe { get; }
    public IReadOnlyList<Candle> Warmup { get; }
    public IReadOnlyList<Candle> Replay { get; }

    public PreparedReplay(
        Timeframe timeframe,
        IEnumerable<Candle> warmup,
        IEnumerable<Candle> replay)
    {
        Timeframe = timeframe;
        Warmup = Array.AsReadOnly(warmup.ToArray());
        Replay = Array.AsReadOnly(replay.ToArray());
        _history = new ReplayHistory(Warmup, Replay);
        _replayStart = Warmup.Count;

        if (Replay.Count == 0)
        {
            throw new ArgumentException("Replay history must be nonempty.", nameof(replay));
        }

        DateTimeOffset? previous = null;

        foreach (Candle candle in Warmup.Concat(Replay))
        {
            if (!CandleValidator.IsValid(candle, timeframe) ||
                (previous.HasValue && candle.OpenTime.UtcTicks - previous.Value.UtcTicks != timeframe.Duration.Ticks))
            {
                throw new ArgumentException("History must be contiguous ordered UTC candles on the timeframe grid.");
            }

            previous = candle.OpenTime;
        }
    }

    private PreparedReplay(
        Timeframe timeframe,
        IReadOnlyList<Candle> warmup,
        IReadOnlyList<Candle> replay,
        IReadOnlyList<Candle> history,
        int replayStart)
    {
        Timeframe = timeframe;
        Warmup = warmup;
        Replay = replay;
        _history = history;
        _replayStart = replayStart;
    }

    /// <summary>
    ///     Takes an exclusively owned read-only history without copying its candle references.
    /// </summary>
    public static PreparedReplay FromOwnedHistory(
        Timeframe timeframe,
        IReadOnlyList<Candle> history,
        int warmupCount)
    {
        if (warmupCount < 0 || warmupCount >= history.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupCount), warmupCount, "Replay history must be nonempty.");
        }

        DateTimeOffset? previous = null;

        foreach (Candle candle in history)
        {
            if (!CandleValidator.IsValid(candle, timeframe) ||
                (previous.HasValue && candle.OpenTime.UtcTicks - previous.Value.UtcTicks != timeframe.Duration.Ticks))
            {
                throw new ArgumentException("History must be contiguous ordered UTC candles on the timeframe grid.");
            }

            previous = candle.OpenTime;
        }

        var warmup = new ReplayRange(history, start: 0, warmupCount);
        var replay = new ReplayRange(history, warmupCount, history.Count - warmupCount);

        return new PreparedReplay(timeframe, warmup, replay, history, warmupCount);
    }

    /// <summary>
    ///     Returns the first replayCount candles with the same warmup, without copying or revalidating history.
    /// </summary>
    public PreparedReplay Slice(int replayCount)
    {
        if (replayCount < 1 || replayCount > Replay.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(replayCount), replayCount, "Replay prefix must be nonempty and within prepared history.");
        }

        return replayCount == Replay.Count
            ? this
            : new PreparedReplay(
                Timeframe,
                Warmup,
                new ReplayPrefix(Replay, replayCount),
                _history,
                _replayStart);
    }

    /// <summary>
    ///     Returns a replay range with immediately preceding warmup, without copying or revalidating history.
    /// </summary>
    public PreparedReplay Window(int start, int count)
    {
        if (start < 0 || start >= Replay.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Window start must be within prepared replay history.");
        }

        if (count < 1 || count > Replay.Count - start)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Window must be nonempty and within prepared replay history.");
        }

        if (start == 0)
        {
            return Slice(count);
        }

        int replayStart = checked(_replayStart + start);
        var warmup = new ReplayRange(_history, replayStart - Warmup.Count, Warmup.Count);
        var replay = new ReplayRange(_history, replayStart, count);

        return new PreparedReplay(Timeframe, warmup, replay, _history, replayStart);
    }
}