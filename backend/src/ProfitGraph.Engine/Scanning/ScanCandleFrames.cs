using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The closed candles of one timeframe aggregated from a pair's contiguous minutes on the timeframe's UTC grid (ADR 0071): what
///     the pair's page reads of that timeframe at the end of a minute. A candle counts once all of its minutes are in the series, so
///     the first one, which the minutes may start inside, and the one still forming are left out.
/// </summary>
public sealed class ScanCandleFrames
{
    private readonly ScanCandle[] _candles;
    private readonly TimeSpan _duration;

    public ScanCandleFrames(IReadOnlyList<ScanCandle> minutes, Timeframe timeframe)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        ArgumentNullException.ThrowIfNull(timeframe);

        _duration = timeframe.Duration;
        var perCandle = (int)(timeframe.Duration / TimeSpan.FromMinutes(1));
        List<ScanCandle> candles = [];
        ScanCandle? current = null;
        var count = 0;

        foreach (ScanCandle minute in minutes)
        {
            DateTimeOffset start = timeframe.FloorToGrid(minute.OpenTime);

            if (current is not null && current.OpenTime != start)
            {
                if (count == perCandle)
                {
                    candles.Add(current);
                }

                current = null;
            }

            if (current is null)
            {
                current = minute with { OpenTime = start };
                count = 1;

                continue;
            }

            current = current with
            {
                High = Math.Max(current.High, minute.High),
                Low = Math.Min(current.Low, minute.Low),
                Close = minute.Close,
                Volume = current.Volume + minute.Volume,
                Turnover = current.Turnover + minute.Turnover
            };
            count++;
        }

        if (current is not null && count == perCandle)
        {
            candles.Add(current);
        }

        _candles = [.. candles];
    }

    /// <summary>
    ///     The last <paramref name="count" /> candles that closed by <paramref name="end" />, oldest first.
    /// </summary>
    public IReadOnlyList<ScanCandle> ClosedBy(DateTimeOffset end, int count)
    {
        var low = 0;
        int high = _candles.Length;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (_candles[middle].OpenTime + _duration <= end)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        int from = Math.Max(val1: 0, low - count);

        return new ArraySegment<ScanCandle>(_candles, from, low - from);
    }
}