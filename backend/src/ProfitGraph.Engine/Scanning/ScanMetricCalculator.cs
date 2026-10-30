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
    private const int ImpulseMinutes = 15;
    private const int RangeMinutes = 4 * MinutesPerHour;
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
        IReadOnlyList<ScanCandle>? range = Range(minutes);

        var metrics = new ScanMetrics
        {
            Price = input.LastPrice ?? close,
            Change5m = Change(minutes, count: 5),
            Change15m = Change(minutes, count: 15),
            Change1h = Change(minutes, MinutesPerHour),
            Change4h = Change(minutes, 4 * MinutesPerHour),
            Change24h = Change(minutes, MinutesPerDay),
            Turnover1h = Turnover(minutes, MinutesPerHour),
            Turnover24h = Turnover(minutes, MinutesPerDay),
            VolumeSpike = VolumeSpike(minutes, hours, MinutesPerHour),
            VolumeSpike5m = VolumeSpike(minutes, hours, count: 5),
            VolumeSpike15m = VolumeSpike(minutes, hours, ImpulseMinutes),
            Natr24h = Natr(hours),
            EfficiencyRatio24h = EfficiencyRatio(hours),
            DistanceToHigh24h = close is { } c1 ? DistanceToHigh(c1, Last(minutes, MinutesPerDay)) : null,
            DistanceToLow24h = close is { } c2 ? DistanceToLow(c2, Last(minutes, MinutesPerDay)) : null,
            DistanceToHigh7d = close is { } c3 ? DistanceToHigh(c3, Week(minutes, hours)) : null,
            DistanceToLow7d = close is { } c4 ? DistanceToLow(c4, Week(minutes, hours)) : null,
            Breakout4h = close is { } c5 ? DistanceToHigh(c5, range) : null,
            Breakdown4h = close is { } c6 ? DistanceToLow(c6, range) : null,
            Range4h = Width(range),
            Spread = Spread(input.BestBid, input.BestAsk),
            ListingAgeDays = input.ListedAt is { } listedAt ? ListingAgeDays(listedAt, input.Now) : null,
            Stale = IsStale(minutes, input.Now)
        };

        return ScanLevels.Apply(metrics, ScanLevels.Of(hours), metrics.Price);
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
    ///     Оборот последних <paramref name="count" /> минут (делитель часа) к медиане часового оборота тех из 167 часов, закрытых до
    ///     начала этих минут, в которые были сделки (ADR 0062): ночь и выходные биржи не тянут медиану к нулю. Медиана
    ///     пересчитывается на окно делением часа на равные части (ADR 0065). Пусто, пока таких часов меньше 72 или их медиана
    ///     нулевая.
    /// </summary>
    private static decimal? VolumeSpike(IReadOnlyList<ScanCandle> minutes, IReadOnlyList<ScanCandle> hours, int count)
    {
        if (minutes.Count < count)
        {
            return null;
        }

        DateTimeOffset windowStart = minutes[^count].OpenTime;

        List<decimal> baseline = hours
            .Where(hour => hour.OpenTime + HourFrame.Duration <= windowStart)
            .TakeLast(SpikeBaselineHours)
            .Where(hour => hour.Volume > 0)
            .Select(hour => hour.Turnover)
            .Order()
            .ToList();

        if (baseline.Count < SpikeMinimumHours)
        {
            return null;
        }

        int middle = baseline.Count / 2;
        decimal median = baseline.Count % 2 == 1 ? baseline[middle] : (baseline[middle - 1] + baseline[middle]) / 2m;

        return median == 0 ? null : Turnover(minutes, count) * (MinutesPerHour / count) / median;
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

    private static decimal? Width(IReadOnlyList<ScanCandle>? window)
    {
        if (window is null)
        {
            return null;
        }

        decimal low = window.Min(candle => candle.Low);

        return (window.Max(candle => candle.High) - low) / low * Percent;
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

    /// <summary>
    ///     Диапазон перед движением (ADR 0066, ADR 0068): последние 240 минут со сделками перед последними 15 минутами. Минуты без
    ///     сделок пропускаются, поэтому ночь и выходные биржи не попадают в диапазон; пусто, пока таких минут меньше 240.
    /// </summary>
    private static IReadOnlyList<ScanCandle>? Range(IReadOnlyList<ScanCandle> minutes)
    {
        var range = new List<ScanCandle>(RangeMinutes);

        for (int i = minutes.Count - ImpulseMinutes - 1; i >= 0 && range.Count < RangeMinutes; i--)
        {
            if (minutes[i].Volume > 0)
            {
                range.Add(minutes[i]);
            }
        }

        return range.Count < RangeMinutes ? null : range;
    }

    /// <summary>
    ///     Последние <paramref name="count" /> свечей; пусто, пока свечей не хватает.
    /// </summary>
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

    internal static void Validate(IReadOnlyList<ScanCandle> candles, Timeframe timeframe, string name)
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