using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Чистые функции метрик сканера (ADR 0059, раздел 3). Все окна скользящие и кончаются последней закрытой свечой;
///     формирующаяся минута задаёт только цену. Метрика, которой не хватает истории, пуста и никогда не считается по
///     укороченному окну. Одинаковые свечи дают одинаковые метрики, поэтому значение сканера воспроизводится по свечам.
/// </summary>
public static class ScanMetricCalculator
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 1440;
    private const int HoursPerDay = 24;
    private const int HoursPerWeek = 168;
    private const int SpikeBaselineHours = 167;
    private const int SpikeMinimumHours = 72;
    private const decimal Percent = 100m;
    private static readonly Timeframe MinuteFrame = Timeframe.Parse("1m");
    private static readonly Timeframe HourFrame = Timeframe.Parse("1h");
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public static ScanMetrics Calculate(ScanInput input)
    {
        Validate(input.Minutes, MinuteFrame, "minute");
        Validate(input.Hours, HourFrame, "hourly");

        IReadOnlyList<ScanCandle> minutes = input.Minutes;
        IReadOnlyList<ScanCandle> hours = input.Hours;
        decimal? close = minutes.Count > 0 ? minutes[^1].Close : null;

        return new ScanMetrics
        {
            Price = input.LastPrice ?? close,
            Change5m = Change(minutes, count: 5),
            Change15m = Change(minutes, count: 15),
            Change1h = Change(minutes, MinutesPerHour),
            Change4h = Change(minutes, 4 * MinutesPerHour),
            Change24h = Change(minutes, MinutesPerDay),
            Turnover1h = Turnover(minutes, MinutesPerHour),
            Turnover24h = Turnover(minutes, MinutesPerDay),
            VolumeSpike = VolumeSpike(minutes, hours),
            Natr24h = Natr(hours),
            EfficiencyRatio24h = EfficiencyRatio(hours),
            DistanceToHigh24h = close is { } c1 ? DistanceToHigh(c1, Last(minutes, MinutesPerDay)) : null,
            DistanceToLow24h = close is { } c2 ? DistanceToLow(c2, Last(minutes, MinutesPerDay)) : null,
            DistanceToHigh7d = close is { } c3 ? DistanceToHigh(c3, Week(minutes, hours)) : null,
            DistanceToLow7d = close is { } c4 ? DistanceToLow(c4, Week(minutes, hours)) : null,
            Spread = Spread(input.BestBid, input.BestAsk),
            ListingAgeDays = input.ListedAt is { } listedAt ? ListingAgeDays(listedAt, input.Now) : null,
            Stale = IsStale(minutes, input.Now)
        };
    }

    /// <summary>
    ///     Изменение за последние <paramref name="count" /> закрытых минут: от открытия первой до закрытия последней, в %.
    /// </summary>
    private static decimal? Change(IReadOnlyList<ScanCandle> minutes, int count)
    {
        if (minutes.Count < count)
        {
            return null;
        }

        decimal start = minutes[^count].Open;

        return (minutes[^1].Close - start) / start * Percent;
    }

    private static decimal? Turnover(IReadOnlyList<ScanCandle> minutes, int count)
    {
        return minutes.Count < count ? null : Last(minutes, count)!.Sum(candle => candle.Turnover);
    }

    /// <summary>
    ///     Оборот последних 60 минут к медиане часового оборота за 167 часов, закрытых до начала этих минут; пусто, пока
    ///     таких часов меньше 72 или их медиана нулевая.
    /// </summary>
    private static decimal? VolumeSpike(IReadOnlyList<ScanCandle> minutes, IReadOnlyList<ScanCandle> hours)
    {
        if (minutes.Count < MinutesPerHour)
        {
            return null;
        }

        DateTimeOffset windowStart = minutes[^MinutesPerHour].OpenTime;

        List<decimal> baseline = hours
            .Where(hour => hour.OpenTime + HourFrame.Duration <= windowStart)
            .TakeLast(SpikeBaselineHours)
            .Select(hour => hour.Turnover)
            .Order()
            .ToList();

        if (baseline.Count < SpikeMinimumHours)
        {
            return null;
        }

        int middle = baseline.Count / 2;
        decimal median = baseline.Count % 2 == 1 ? baseline[middle] : (baseline[middle - 1] + baseline[middle]) / 2m;

        return median == 0 ? null : Turnover(minutes, MinutesPerHour) / median;
    }

    /// <summary>
    ///     Простое среднее истинного диапазона к закрытию по 24 закрытым часам, в %; первому часу нужен предыдущий.
    /// </summary>
    private static decimal? Natr(IReadOnlyList<ScanCandle> hours)
    {
        if (hours.Count < HoursPerDay + 1)
        {
            return null;
        }

        decimal sum = 0;

        for (int i = hours.Count - HoursPerDay; i < hours.Count; i++)
        {
            ScanCandle hour = hours[i];
            decimal previousClose = hours[i - 1].Close;
            decimal trueRange = Math.Max(hour.High, previousClose) - Math.Min(hour.Low, previousClose);
            sum += trueRange / hour.Close;
        }

        return sum / HoursPerDay * Percent;
    }

    /// <summary>
    ///     Коэффициент эффективности Кауфмана по часовым закрытиям за 24 часа: 0 — пила, 1 — тренд; без движения — 0.
    /// </summary>
    private static decimal? EfficiencyRatio(IReadOnlyList<ScanCandle> hours)
    {
        if (hours.Count < HoursPerDay + 1)
        {
            return null;
        }

        decimal path = 0;

        for (int i = hours.Count - HoursPerDay; i < hours.Count; i++)
        {
            path += Math.Abs(hours[i].Close - hours[i - 1].Close);
        }

        decimal net = Math.Abs(hours[^1].Close - hours[^(HoursPerDay + 1)].Close);

        return path == 0 ? 0 : net / path;
    }

    private static decimal? DistanceToHigh(decimal close, IReadOnlyList<ScanCandle>? window)
    {
        if (window is null)
        {
            return null;
        }

        decimal high = window.Max(candle => candle.High);

        return (close - high) / high * Percent;
    }

    private static decimal? DistanceToLow(decimal close, IReadOnlyList<ScanCandle>? window)
    {
        if (window is null)
        {
            return null;
        }

        decimal low = window.Min(candle => candle.Low);

        return (close - low) / low * Percent;
    }

    /// <summary>
    ///     Окно семи дней: 168 закрытых часов и минуты, закрытые после последнего из них; пусто, пока часов меньше 168.
    /// </summary>
    private static IReadOnlyList<ScanCandle>? Week(IReadOnlyList<ScanCandle> minutes, IReadOnlyList<ScanCandle> hours)
    {
        if (hours.Count < HoursPerWeek)
        {
            return null;
        }

        DateTimeOffset lastHourEnd = hours[^1].OpenTime + HourFrame.Duration;

        return [.. Last(hours, HoursPerWeek)!, .. minutes.Where(minute => minute.OpenTime >= lastHourEnd)];
    }

    private static decimal? Spread(decimal? bid, decimal? ask)
    {
        if (bid is not { } b || ask is not { } a || b <= 0 || a < b)
        {
            return null;
        }

        return (a - b) / ((a + b) / 2m) * Percent;
    }

    private static int ListingAgeDays(DateTimeOffset listedAt, DateTimeOffset now)
    {
        return (int)Math.Max(val1: 0, (now - listedAt).Ticks / TimeSpan.TicksPerDay);
    }

    /// <summary>
    ///     Сделок не было дольше 15 минут: последняя минута с объёмом закончилась раньше или такой минуты нет.
    /// </summary>
    private static bool IsStale(IReadOnlyList<ScanCandle> minutes, DateTimeOffset now)
    {
        for (int i = minutes.Count - 1; i >= 0; i--)
        {
            if (minutes[i].Volume > 0)
            {
                return now - (minutes[i].OpenTime + MinuteFrame.Duration) > StaleAfter;
            }
        }

        return true;
    }

    private static IReadOnlyList<ScanCandle>? Last(IReadOnlyList<ScanCandle> candles, int count)
    {
        if (candles.Count < count)
        {
            return null;
        }

        var window = new ScanCandle[count];

        for (var i = 0; i < count; i++)
        {
            window[i] = candles[candles.Count - count + i];
        }

        return window;
    }

    private static void Validate(IReadOnlyList<ScanCandle> candles, Timeframe timeframe, string name)
    {
        for (var i = 0; i < candles.Count; i++)
        {
            ScanCandle candle = candles[i];

            bool valid = candle.OpenTime.Offset == TimeSpan.Zero && timeframe.IsAligned(candle.OpenTime) &&
                candle.Open > 0 && candle.Low > 0 && candle.Close > 0 && candle.Volume >= 0 && candle.Turnover >= 0 &&
                candle.High >= Math.Max(candle.Open, candle.Close) && candle.Low <= Math.Min(candle.Open, candle.Close);

            if (!valid)
            {
                throw new ScanInputException($"The {name} candle at {candle.OpenTime:O} is not a valid candle on its grid.");
            }

            if (i > 0 && candle.OpenTime != candles[i - 1].OpenTime + timeframe.Duration)
            {
                throw new ScanInputException($"The {name} candles are not consecutive at {candle.OpenTime:O}.");
            }
        }
    }
}