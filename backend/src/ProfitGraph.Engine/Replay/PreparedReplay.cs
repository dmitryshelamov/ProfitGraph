using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Immutable history validated once and safely shared by independent replay trials. Candles are ordered on the timeframe
///     grid and contiguous except where market closures cover the gap (ADR 0023, TC1).
/// </summary>
public sealed class PreparedReplay
{
    private readonly IReadOnlyList<Candle> _history;
    private readonly int _replayStart;

    public Timeframe Timeframe { get; }
    public IReadOnlyList<Candle> Warmup { get; }
    public IReadOnlyList<Candle> Replay { get; }

    /// <summary>
    ///     The ordered closures that cover the gaps of this history; empty for a market that never closes.
    /// </summary>
    public IReadOnlyList<MarketClosure> Closures { get; }

    public PreparedReplay(
        Timeframe timeframe,
        IEnumerable<Candle> warmup,
        IEnumerable<Candle> replay)
        : this(timeframe, warmup, replay, [])
    {
    }

    public PreparedReplay(
        Timeframe timeframe,
        IEnumerable<Candle> warmup,
        IEnumerable<Candle> replay,
        IEnumerable<MarketClosure> closures)
    {
        Timeframe = timeframe;
        Warmup = Array.AsReadOnly(warmup.ToArray());
        Replay = Array.AsReadOnly(replay.ToArray());
        Closures = Array.AsReadOnly(closures.ToArray());
        _history = new ReplayHistory(Warmup, Replay);
        _replayStart = Warmup.Count;

        if (Replay.Count == 0)
        {
            throw new ArgumentException("Replay history must be nonempty.", nameof(replay));
        }

        Validate(timeframe, Warmup.Concat(Replay), Closures);
    }

    private PreparedReplay(
        Timeframe timeframe,
        IReadOnlyList<Candle> warmup,
        IReadOnlyList<Candle> replay,
        IReadOnlyList<Candle> history,
        int replayStart,
        IReadOnlyList<MarketClosure> closures)
    {
        Timeframe = timeframe;
        Warmup = warmup;
        Replay = replay;
        Closures = closures;
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
        return FromOwnedHistory(timeframe, history, warmupCount, []);
    }

    /// <summary>
    ///     Takes an exclusively owned read-only history whose gaps the given closures cover.
    /// </summary>
    public static PreparedReplay FromOwnedHistory(
        Timeframe timeframe,
        IReadOnlyList<Candle> history,
        int warmupCount,
        IEnumerable<MarketClosure> closures)
    {
        if (warmupCount < 0 || warmupCount >= history.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupCount), warmupCount, "Replay history must be nonempty.");
        }

        IReadOnlyList<MarketClosure> frozen = Array.AsReadOnly(closures.ToArray());
        Validate(timeframe, history, frozen);
        var warmup = new ReplayRange(history, start: 0, warmupCount);
        var replay = new ReplayRange(history, warmupCount, history.Count - warmupCount);

        return new PreparedReplay(timeframe, warmup, replay, history, warmupCount, frozen);
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
                _replayStart,
                Closures);
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

        return new PreparedReplay(Timeframe, warmup, replay, _history, replayStart, Closures);
    }

    /// <summary>
    ///     Requires valid candles in strictly increasing grid order, each gap covered completely by closures that are
    ///     ordered, aligned and disjoint, and no candle inside a closure.
    /// </summary>
    private static void Validate(Timeframe timeframe, IEnumerable<Candle> history, IReadOnlyList<MarketClosure> closures)
    {
        for (var index = 0; index < closures.Count; index++)
        {
            MarketClosure closure = closures[index];

            if (closure.From.Offset != TimeSpan.Zero || closure.To.Offset != TimeSpan.Zero || closure.From >= closure.To ||
                !timeframe.IsAligned(closure.From) || !timeframe.IsAligned(closure.To) || (index > 0 && closure.From < closures[index - 1].To))
            {
                throw new ArgumentException("Market closures must be ordered, disjoint, nonempty UTC ranges on the timeframe grid.");
            }
        }

        var next = 0;
        DateTimeOffset? expected = null;

        foreach (Candle candle in history)
        {
            if (!CandleValidator.IsValid(candle, timeframe) || (expected.HasValue && !IsCovered(closures, ref next, expected.Value, candle.OpenTime)))
            {
                throw new ArgumentException("History must be ordered UTC candles on the timeframe grid, contiguous except where market closures cover the gap.");
            }

            while (next < closures.Count && closures[next].To <= candle.OpenTime)
            {
                next++;
            }

            if (next < closures.Count && closures[next].From <= candle.OpenTime)
            {
                throw new ArgumentException("A candle lies inside a market closure.");
            }

            expected = candle.OpenTime + timeframe.Duration;
        }
    }

    private static bool IsCovered(IReadOnlyList<MarketClosure> closures, ref int next, DateTimeOffset from, DateTimeOffset to)
    {
        DateTimeOffset cursor = from;

        while (cursor < to)
        {
            while (next < closures.Count && closures[next].To <= cursor)
            {
                next++;
            }

            if (next == closures.Count || closures[next].From > cursor)
            {
                return false;
            }

            cursor = closures[next].To;
        }

        return cursor == to;
    }
}