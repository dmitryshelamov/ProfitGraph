using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Replays one pair's contiguous closed minutes and gives the metrics the live scanner computes at the end of each minute
///     (ADR 0059): the last 1,440 minutes, the closed hours aggregated from the minutes since the first hour boundary (the last 168),
///     the minute's close as the price, no quote (the spread stays empty) and the listing time when it is known. After a linear
///     preparation every minute costs constant time, and <see cref="ScanMetricCalculator" /> on the same window gives the same values.
/// </summary>
public sealed class ScanReplay
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 1440;
    private const int HoursPerDay = 24;
    private const int HoursPerWeek = 168;
    private const int SpikeBaselineHours = 167;
    private const int SpikeMinimumHours = 72;
    private const int ImpulseMinutes = 15;
    private const int RangeMinutes = 4 * MinutesPerHour;
    private const decimal Percent = 100m;
    private static readonly Timeframe MinuteFrame = Timeframe.Parse("1m");
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private readonly ScanCandle[] _minutes;
    private readonly decimal[] _turnoverSums;
    private readonly decimal[] _dayHighs;
    private readonly decimal[] _dayLows;
    private readonly decimal[] _rangeHighs;
    private readonly decimal[] _rangeLows;
    private readonly int[] _tradedBefore;
    private readonly decimal[] _hourHighsSoFar;
    private readonly decimal[] _hourLowsSoFar;
    private readonly int[] _lastTraded;
    private readonly int _firstHourMinute;
    private readonly ScanCandle[] _hours;
    private readonly decimal?[] _natr;
    private readonly decimal?[] _efficiency;
    private readonly decimal[] _weekHighs;
    private readonly decimal[] _weekLows;
    private readonly decimal?[] _spikeMedians;
    private readonly DateTimeOffset? _listedAt;
    private readonly IReadOnlyList<decimal>?[] _levels;

    public int Count => _minutes.Length;

    public IReadOnlyList<ScanCandle> Minutes => _minutes;

    public ScanReplay(IReadOnlyList<ScanCandle> minutes, DateTimeOffset? listedAt)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        ScanMetricCalculator.Validate(minutes, MinuteFrame, "minute");

        _minutes = [.. minutes];
        _listedAt = listedAt;
        int count = _minutes.Length;
        _turnoverSums = new decimal[count + 1];
        _lastTraded = new int[count];
        int lastTraded = -1;

        for (var i = 0; i < count; i++)
        {
            _turnoverSums[i + 1] = _turnoverSums[i] + _minutes[i].Turnover;
            lastTraded = _minutes[i].Volume > 0 ? i : lastTraded;
            _lastTraded[i] = lastTraded;
        }

        decimal[] highs = [.. _minutes.Select(minute => minute.High)];
        decimal[] lows = [.. _minutes.Select(minute => minute.Low)];
        _dayHighs = SlidingExtreme(highs, MinutesPerDay, highest: true);
        _dayLows = SlidingExtreme(lows, MinutesPerDay, highest: false);
        int[] traded = [.. Enumerable.Range(start: 0, count).Where(i => _minutes[i].Volume > 0)];
        _tradedBefore = new int[count + 1];

        for (var i = 0; i < count; i++)
        {
            _tradedBefore[i + 1] = _tradedBefore[i] + (_minutes[i].Volume > 0 ? 1 : 0);
        }

        // The range runs over the minutes with trades only, indexed by their position among them.
        _rangeHighs = SlidingExtreme([.. traded.Select(i => highs[i])], RangeMinutes, highest: true);
        _rangeLows = SlidingExtreme([.. traded.Select(i => lows[i])], RangeMinutes, highest: false);
        _firstHourMinute = Array.FindIndex(_minutes, minute => minute.OpenTime.UtcTicks % TimeSpan.TicksPerHour == 0);
        _hourHighsSoFar = new decimal[count];
        _hourLowsSoFar = new decimal[count];

        for (var i = 0; i < count; i++)
        {
            bool hourStart = i == 0 || _minutes[i].OpenTime.UtcTicks % TimeSpan.TicksPerHour == 0;
            _hourHighsSoFar[i] = hourStart ? _minutes[i].High : Math.Max(_hourHighsSoFar[i - 1], _minutes[i].High);
            _hourLowsSoFar[i] = hourStart ? _minutes[i].Low : Math.Min(_hourLowsSoFar[i - 1], _minutes[i].Low);
        }

        _hours = _firstHourMinute < 0 ? [] : Hours(_minutes, _firstHourMinute);
        _natr = new decimal?[_hours.Length];
        _efficiency = new decimal?[_hours.Length];

        for (int k = HoursPerDay; k < _hours.Length; k++)
        {
            _natr[k] = Natr(_hours, k);
            _efficiency[k] = Efficiency(_hours, k);
        }

        _weekHighs = SlidingExtreme(_hours.Select(hour => hour.High).ToArray(), HoursPerWeek, highest: true);
        _weekLows = SlidingExtreme(_hours.Select(hour => hour.Low).ToArray(), HoursPerWeek, highest: false);
        _spikeMedians = new decimal?[_hours.Length];

        for (var j = 0; j < _hours.Length; j++)
        {
            _spikeMedians[j] = Median(_hours, j);
        }

        _levels = new IReadOnlyList<decimal>?[_hours.Length];
    }

    /// <summary>
    ///     The end of the minute at <paramref name="index" />: the moment its metrics describe.
    /// </summary>
    public DateTimeOffset EndOf(int index)
    {
        return _minutes[index].OpenTime + MinuteFrame.Duration;
    }

    /// <summary>
    ///     The metrics at the end of the minute at <paramref name="index" />.
    /// </summary>
    public ScanMetrics At(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _minutes.Length);

        DateTimeOffset now = EndOf(index);
        int window = Math.Min(MinutesPerDay, index + 1);
        decimal close = _minutes[index].Close;
        int lastHour = LastClosedHour(index);
        int hourCount = lastHour < 0 ? 0 : Math.Min(HoursPerWeek, lastHour + 1);
        int? range = RangeEnd(index, window);

        var metrics = new ScanMetrics
        {
            Price = close,
            Change5m = Change(index, window, count: 5),
            Change15m = Change(index, window, count: 15),
            Change1h = Change(index, window, MinutesPerHour),
            Change4h = Change(index, window, 4 * MinutesPerHour),
            Change24h = Change(index, window, MinutesPerDay),
            Turnover1h = Turnover(index, window, MinutesPerHour),
            Turnover24h = Turnover(index, window, MinutesPerDay),
            VolumeSpike = VolumeSpike(index, window, MinutesPerHour),
            VolumeSpike5m = VolumeSpike(index, window, count: 5),
            VolumeSpike15m = VolumeSpike(index, window, ImpulseMinutes),
            Natr24h = hourCount >= HoursPerDay + 1 ? _natr[lastHour] : null,
            EfficiencyRatio24h = hourCount >= HoursPerDay + 1 ? _efficiency[lastHour] : null,
            DistanceToHigh24h = window >= MinutesPerDay ? (close - _dayHighs[index]) / _dayHighs[index] * Percent : null,
            DistanceToLow24h = window >= MinutesPerDay ? (close - _dayLows[index]) / _dayLows[index] * Percent : null,
            DistanceToHigh7d = hourCount >= HoursPerWeek ? (close - WeekHigh(index, lastHour)) / WeekHigh(index, lastHour) * Percent : null,
            DistanceToLow7d = hourCount >= HoursPerWeek ? (close - WeekLow(index, lastHour)) / WeekLow(index, lastHour) * Percent : null,
            Breakout4h = range is { } high ? (close - _rangeHighs[high]) / _rangeHighs[high] * Percent : null,
            Breakdown4h = range is { } low ? (close - _rangeLows[low]) / _rangeLows[low] * Percent : null,
            Range4h = range is { } end ? (_rangeHighs[end] - _rangeLows[end]) / _rangeLows[end] * Percent : null,
            Spread = null,
            ListingAgeDays = _listedAt is { } listedAt ? (int)Math.Max(val1: 0, (now - listedAt).Ticks / TimeSpan.TicksPerDay) : null,
            Stale = IsStale(index, window, now)
        };

        return lastHour < 0 ? metrics : ScanLevels.Apply(metrics, Levels(lastHour), close);
    }

    /// <summary>
    ///     The levels of the 168 hours up to <paramref name="lastHour" />, drawn once per hour.
    /// </summary>
    private IReadOnlyList<decimal> Levels(int lastHour)
    {
        return _levels[lastHour] ??=
            ScanLevels.Of(new ArraySegment<ScanCandle>(_hours, Math.Max(val1: 0, lastHour - HoursPerWeek + 1), Math.Min(HoursPerWeek, lastHour + 1)));
    }

    /// <summary>
    ///     The closed hours the live scanner holds at the end of the minute at <paramref name="index" />, oldest first, at most 168.
    /// </summary>
    public IReadOnlyList<ScanCandle> HoursAt(int index)
    {
        int lastHour = LastClosedHour(index);

        if (lastHour < 0)
        {
            return [];
        }

        int first = Math.Max(val1: 0, lastHour - HoursPerWeek + 1);

        return _hours[first..(lastHour + 1)];
    }

    private static ScanCandle[] Hours(ScanCandle[] minutes, int firstHourMinute)
    {
        int count = (minutes.Length - firstHourMinute) / MinutesPerHour;
        var hours = new ScanCandle[count];

        for (var k = 0; k < count; k++)
        {
            int start = firstHourMinute + k * MinutesPerHour;
            var hour = new ArraySegment<ScanCandle>(minutes, start, MinutesPerHour);
            hours[k] = new ScanCandle(
                minutes[start].OpenTime,
                minutes[start].Open,
                hour.Max(minute => minute.High),
                hour.Min(minute => minute.Low),
                minutes[start + MinutesPerHour - 1].Close,
                hour.Sum(minute => minute.Volume),
                hour.Sum(minute => minute.Turnover));
        }

        return hours;
    }

    /// <summary>
    ///     For each position, the highest or lowest of the last <paramref name="size" /> values ending there, by a monotonic queue.
    /// </summary>
    private static decimal[] SlidingExtreme(decimal[] values, int size, bool highest)
    {
        var result = new decimal[values.Length];
        var queue = new LinkedList<int>();

        for (var i = 0; i < values.Length; i++)
        {
            while (queue.Count > 0 && (highest ? values[queue.Last!.Value] <= values[i] : values[queue.Last!.Value] >= values[i]))
            {
                queue.RemoveLast();
            }

            queue.AddLast(i);

            if (queue.First!.Value <= i - size)
            {
                queue.RemoveFirst();
            }

            result[i] = values[queue.First!.Value];
        }

        return result;
    }

    private static decimal Natr(ScanCandle[] hours, int last)
    {
        decimal sum = 0;

        for (int i = last - HoursPerDay + 1; i <= last; i++)
        {
            ScanCandle hour = hours[i];
            decimal previousClose = hours[i - 1].Close;
            decimal trueRange = Math.Max(hour.High, previousClose) - Math.Min(hour.Low, previousClose);
            sum += trueRange / hour.Close;
        }

        return sum / HoursPerDay * Percent;
    }

    private static decimal Efficiency(ScanCandle[] hours, int last)
    {
        decimal path = 0;

        for (int i = last - HoursPerDay + 1; i <= last; i++)
        {
            path += Math.Abs(hours[i].Close - hours[i - 1].Close);
        }

        decimal net = Math.Abs(hours[last].Close - hours[last - HoursPerDay].Close);

        return path == 0 ? 0 : net / path;
    }

    /// <summary>
    ///     The median turnover of the hours with trades among the last 167 hours up to the hour at <paramref name="last" />; empty below
    ///     72 such hours or when it is zero.
    /// </summary>
    private static decimal? Median(ScanCandle[] hours, int last)
    {
        int first = Math.Max(val1: 0, last - SpikeBaselineHours + 1);
        decimal[] baseline = [.. hours[first..(last + 1)].Where(hour => hour.Volume > 0).Select(hour => hour.Turnover).Order()];
        int count = baseline.Length;

        if (count < SpikeMinimumHours)
        {
            return null;
        }

        int middle = count / 2;
        decimal median = count % 2 == 1 ? baseline[middle] : (baseline[middle - 1] + baseline[middle]) / 2m;

        return median == 0 ? null : median;
    }

    /// <summary>
    ///     The last hour whose sixty minutes end by the end of the minute at <paramref name="index" />; -1 before the first.
    /// </summary>
    private int LastClosedHour(int index)
    {
        if (_firstHourMinute < 0 || index < _firstHourMinute)
        {
            return -1;
        }

        return (index - _firstHourMinute + 1) / MinutesPerHour - 1;
    }

    private decimal? Change(int index, int window, int count)
    {
        if (window < count)
        {
            return null;
        }

        decimal start = _minutes[index - count + 1].Open;

        return (_minutes[index].Close - start) / start * Percent;
    }

    private decimal? Turnover(int index, int window, int count)
    {
        return window < count ? null : _turnoverSums[index + 1] - _turnoverSums[index + 1 - count];
    }

    private decimal? VolumeSpike(int index, int window, int count)
    {
        if (window < count || _firstHourMinute < 0)
        {
            return null;
        }

        // The baseline is the hours that end by the start of the last count minutes.
        int baselineLast = index - count - MinutesPerHour + 1 - _firstHourMinute is var offset and >= 0 ? offset / MinutesPerHour : -1;

        if (baselineLast < 0 || _spikeMedians[baselineLast] is not { } median)
        {
            return null;
        }

        return Turnover(index, window, count) * (MinutesPerHour / count) / median;
    }

    /// <summary>
    ///     The position, among the minutes with trades, of the last one before the window's last fifteen minutes, when the window holds
    ///     240 such minutes before them; null otherwise.
    /// </summary>
    private int? RangeEnd(int index, int window)
    {
        int end = index - ImpulseMinutes;
        int start = index - window + 1;

        if (end < start || _tradedBefore[end + 1] - _tradedBefore[start] < RangeMinutes)
        {
            return null;
        }

        return _tradedBefore[end + 1] - 1;
    }

    private decimal WeekHigh(int index, int lastHour)
    {
        return MinutesAfterHour(index, lastHour) ? Math.Max(_weekHighs[lastHour], _hourHighsSoFar[index]) : _weekHighs[lastHour];
    }

    private decimal WeekLow(int index, int lastHour)
    {
        return MinutesAfterHour(index, lastHour) ? Math.Min(_weekLows[lastHour], _hourLowsSoFar[index]) : _weekLows[lastHour];
    }

    /// <summary>
    ///     Whether minutes of the forming hour follow the last closed hour.
    /// </summary>
    private bool MinutesAfterHour(int index, int lastHour)
    {
        return index > _firstHourMinute + (lastHour + 1) * MinutesPerHour - 1;
    }

    private bool IsStale(int index, int window, DateTimeOffset now)
    {
        int traded = _lastTraded[index];

        return traded < index - window + 1 || now - (_minutes[traded].OpenTime + MinuteFrame.Duration) > StaleAfter;
    }
}