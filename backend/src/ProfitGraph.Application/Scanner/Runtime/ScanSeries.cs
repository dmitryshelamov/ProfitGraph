using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Runtime;

/// <summary>
///     One pair's candles in the scanner's memory (ADR 0059, section 2): the last 1,440 closed minutes and the last 168 closed
///     hours, contiguous, and the price of the forming minute. Closed minutes that arrive before the warm-up or during a gap are
///     held back and merged with the REST history; the merge fills holes with flat candles, because a minute the venue reports
///     nowhere had no trades. While connected, a minute the stream skips is flat too, and a real candle for it replaces the flat
///     one. Hours after the last REST hour are aggregated from the minutes. Safe for one writer per pair and concurrent readers.
/// </summary>
public sealed class ScanSeries(string symbol)
{
    public const int MinuteCapacity = 1440;
    public const int HourCapacity = 168;

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly Lock _lock = new Lock();
    private readonly SortedDictionary<DateTimeOffset, ScanCandle> _held = new SortedDictionary<DateTimeOffset, ScanCandle>();
    private List<ScanCandle> _minutes = [];
    private List<ScanCandle> _hours = [];
    private ScanCandle? _forming;
    private bool _warm;
    private bool _gap;
    private DateTimeOffset? _listedAt;

    public string Symbol { get; } = symbol;

    public bool IsWarm
    {
        get
        {
            lock (_lock)
            {
                return _warm;
            }
        }
    }

    /// <summary>
    ///     The series lost candles (a reconnect) and waits for a REST backfill; its row is shown as incomplete.
    /// </summary>
    public bool HasGap
    {
        get
        {
            lock (_lock)
            {
                return _warm && _gap;
            }
        }
    }

    /// <summary>
    ///     The first minute and the first hour a backfill must read, or null before the warm-up.
    /// </summary>
    public (DateTimeOffset Minutes, DateTimeOffset Hours)? BackfillFrom
    {
        get
        {
            lock (_lock)
            {
                if (!_warm || _minutes.Count == 0 || _hours.Count == 0)
                {
                    return null;
                }

                return (_minutes[^1].OpenTime + Minute, _hours[^1].OpenTime + Hour);
            }
        }
    }

    public void Accept(ScanCandleUpdateInternal update)
    {
        lock (_lock)
        {
            ScanCandle candle = update.Candle;

            if (!update.Closed)
            {
                if (_minutes.Count == 0 || candle.OpenTime > _minutes[^1].OpenTime)
                {
                    _forming = candle;
                }

                return;
            }

            if (!_warm || _gap)
            {
                _held[candle.OpenTime] = candle;

                return;
            }

            if (_minutes.Count == 0)
            {
                Append(candle);

                return;
            }

            DateTimeOffset last = _minutes[^1].OpenTime;

            if (candle.OpenTime <= last)
            {
                ReplaceFlat(candle);

                return;
            }

            while (_minutes[^1].OpenTime + Minute < candle.OpenTime)
            {
                Append(Flat(_minutes[^1].Close, _minutes[^1].OpenTime + Minute));
            }

            Append(candle);
        }
    }

    /// <summary>
    ///     The connection was lost: closed minutes are held back until a REST backfill closes the gap.
    /// </summary>
    public void MarkGap()
    {
        lock (_lock)
        {
            _gap = _warm;
        }
    }

    /// <summary>
    ///     Merges REST history that covers everything before <paramref name="upTo" /> with what the series holds and what was
    ///     held back, then shows the series. A known listing time is kept.
    /// </summary>
    public void Merge(IReadOnlyList<ScanCandle> minutes, IReadOnlyList<ScanCandle> hours, DateTimeOffset? listedAt)
    {
        lock (_lock)
        {
            var allMinutes = new SortedDictionary<DateTimeOffset, ScanCandle>();
            Add(allMinutes, _minutes);
            Add(allMinutes, minutes);
            Add(allMinutes, _held.Values);
            _held.Clear();

            var allHours = new SortedDictionary<DateTimeOffset, ScanCandle>();
            Add(allHours, _hours);
            Add(allHours, hours);

            _minutes = Contiguous(allMinutes, Minute, MinuteCapacity);
            _hours = Contiguous(allHours, Hour, HourCapacity);
            RollHours();
            _listedAt = listedAt ?? _listedAt;
            _warm = true;
            _gap = false;
        }
    }

    /// <summary>
    ///     Adds flat minutes up to the minute before <paramref name="upTo" /> when the stream sent none for them while connected.
    /// </summary>
    public void AdvanceTo(DateTimeOffset upTo)
    {
        lock (_lock)
        {
            if (!_warm || _gap || _minutes.Count == 0)
            {
                return;
            }

            while (_minutes[^1].OpenTime + Minute < upTo)
            {
                Append(Flat(_minutes[^1].Close, _minutes[^1].OpenTime + Minute));
            }
        }
    }

    /// <summary>
    ///     The metric input at <paramref name="now" />, or null before the warm-up.
    /// </summary>
    public ScanInput? Input(DateTimeOffset now, ScanQuoteInternal? quote)
    {
        lock (_lock)
        {
            if (!_warm)
            {
                return null;
            }

            decimal? formingPrice = _forming is { } forming && (_minutes.Count == 0 || forming.OpenTime > _minutes[^1].OpenTime) ? forming.Close : null;

            return new ScanInput([.. _minutes], [.. _hours], formingPrice, quote?.BestBid, quote?.BestAsk, _listedAt, now);
        }
    }

    private static void Add(SortedDictionary<DateTimeOffset, ScanCandle> all, IEnumerable<ScanCandle> candles)
    {
        foreach (ScanCandle candle in candles)
        {
            all[candle.OpenTime] = candle;
        }
    }

    /// <summary>
    ///     The last <paramref name="capacity" /> intervals up to the newest candle, holes filled flat from the previous close.
    /// </summary>
    private static List<ScanCandle> Contiguous(SortedDictionary<DateTimeOffset, ScanCandle> all, TimeSpan step, int capacity)
    {
        if (all.Count == 0)
        {
            return [];
        }

        DateTimeOffset last = all.Keys.Last();
        DateTimeOffset first = all.Keys.First();
        DateTimeOffset start = last - (capacity - 1) * step;

        if (start < first)
        {
            start = first;
        }

        var series = new List<ScanCandle>(capacity);
        ScanCandle? previous = all.Values.LastOrDefault(candle => candle.OpenTime < start);

        for (DateTimeOffset time = start; time <= last; time += step)
        {
            if (all.TryGetValue(time, out ScanCandle? candle))
            {
                series.Add(candle);
                previous = candle;
            }
            else if (previous is not null)
            {
                previous = Flat(previous.Close, time);
                series.Add(previous);
            }
        }

        return series;
    }

    private static ScanCandle Flat(decimal close, DateTimeOffset openTime)
    {
        return new ScanCandle(openTime, close, close, close, close, Volume: 0, Turnover: 0);
    }

    private void Append(ScanCandle candle)
    {
        _minutes.Add(candle);

        if (_minutes.Count > MinuteCapacity)
        {
            _minutes.RemoveRange(index: 0, _minutes.Count - MinuteCapacity);
        }

        if ((candle.OpenTime + Minute).UtcTicks % Hour.Ticks == DateTimeOffset.UnixEpoch.UtcTicks % Hour.Ticks)
        {
            RollHours();
        }
    }

    /// <summary>
    ///     A real candle for a minute the series filled flat replaces it, and the hour it belongs to is aggregated again.
    /// </summary>
    private void ReplaceFlat(ScanCandle candle)
    {
        var index = (int)((candle.OpenTime - _minutes[0].OpenTime) / Minute);

        if (index < 0 || _minutes[index].Volume != 0 || candle.Volume == 0)
        {
            return;
        }

        _minutes[index] = candle;
        DateTimeOffset hourStart = Floor(candle.OpenTime, Hour);
        int hourIndex = _hours.FindIndex(hour => hour.OpenTime == hourStart);

        if (hourIndex >= 0 && Aggregate(hourStart) is { } hour)
        {
            _hours[hourIndex] = hour;
        }
    }

    /// <summary>
    ///     Appends every hour after the last held hour whose sixty minutes the series holds.
    /// </summary>
    private void RollHours()
    {
        if (_minutes.Count == 0)
        {
            return;
        }

        DateTimeOffset next = _hours.Count > 0 ? _hours[^1].OpenTime + Hour : Ceiling(_minutes[0].OpenTime, Hour);

        if (next < _minutes[0].OpenTime)
        {
            // The minutes no longer reach the hour after the last held one: the hours restart from the minutes.
            _hours = [];
            next = Ceiling(_minutes[0].OpenTime, Hour);
        }

        while (Aggregate(next) is { } hour)
        {
            _hours.Add(hour);
            next += Hour;
        }

        if (_hours.Count > HourCapacity)
        {
            _hours.RemoveRange(index: 0, _hours.Count - HourCapacity);
        }
    }

    private ScanCandle? Aggregate(DateTimeOffset hourStart)
    {
        var index = (int)((hourStart - _minutes[0].OpenTime) / Minute);

        if (index < 0 || index + 59 >= _minutes.Count)
        {
            return null;
        }

        List<ScanCandle> minutes = _minutes.GetRange(index, count: 60);

        return new ScanCandle(
            hourStart,
            minutes[0].Open,
            minutes.Max(minute => minute.High),
            minutes.Min(minute => minute.Low),
            minutes[^1].Close,
            minutes.Sum(minute => minute.Volume),
            minutes.Sum(minute => minute.Turnover));
    }

    private static DateTimeOffset Floor(DateTimeOffset time, TimeSpan step)
    {
        return new DateTimeOffset(time.UtcTicks - (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % step.Ticks, TimeSpan.Zero);
    }

    private static DateTimeOffset Ceiling(DateTimeOffset time, TimeSpan step)
    {
        DateTimeOffset floor = Floor(time, step);

        return floor == time ? floor : floor + step;
    }
}