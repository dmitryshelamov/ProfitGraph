using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     The replay (ADR 0059, section 6) gives at every minute exactly what <see cref="ScanMetricCalculator" /> gives for the window the
///     live scanner holds then: the last 1,440 minutes and the last 168 closed hours. The series starts off an hour boundary and has
///     runs of minutes without trades.
/// </summary>
public sealed class ScanReplayTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 17, second: 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Test]
    public void EveryMinuteMatchesTheCalculatorOnTheLiveWindow()
    {
        List<ScanCandle> minutes = RandomMinutes(count: 13_700, seed: 7);
        DateTimeOffset listedAt = Start.AddDays(-2);
        var replay = new ScanReplay(minutes, listedAt);
        int[] edges = [0, 1, 4, 5, 42, 43, 44, 102, 103, 162, 163, 1438, 1439, 1440, 1441, 4362, 4363, 10_122, 10_123, 10_124, 13_699];

        foreach (int index in edges.Concat(Enumerable.Range(start: 0, count: 13_700).Where(index => index % 29 == 0)).Distinct())
        {
            List<ScanCandle> window = minutes[Math.Max(val1: 0, index - 1439)..(index + 1)];
            var input = new ScanInput(window, replay.HoursAt(index), LastPrice: null, BestBid: null, BestAsk: null, listedAt, replay.EndOf(index));

            replay.At(index).ShouldBe(ScanMetricCalculator.Calculate(input), $"minute {index}");
        }
    }

    [Test]
    public void AMarketWithNightsAndWeekendsMatchesTheCalculatorToo()
    {
        // MOEX-like sessions (ADR 0062): trades from 04:00 to 20:50 UTC on weekdays only; the scanner fills the rest flat.
        List<ScanCandle> minutes = Sessions(RandomMinutes(count: 14_400, seed: 11));
        var replay = new ScanReplay(minutes, listedAt: null);

        foreach (int index in Enumerable.Range(start: 0, count: 14_400).Where(index => index % 37 == 0 || index == 14_399))
        {
            List<ScanCandle> window = minutes[Math.Max(val1: 0, index - 1439)..(index + 1)];
            var input = new ScanInput(window, replay.HoursAt(index), LastPrice: null, BestBid: null, BestAsk: null, ListedAt: null, replay.EndOf(index));

            replay.At(index).ShouldBe(ScanMetricCalculator.Calculate(input), $"minute {index}");
        }

        replay.At(index: 14_399).VolumeSpike.ShouldNotBeNull();
    }

    [Test]
    public void LaterMetricsAppearOnceTheirHistoryIsLongEnough()
    {
        var replay = new ScanReplay(RandomMinutes(count: 13_700, seed: 3), listedAt: null);

        replay.At(index: 3).Change5m.ShouldBeNull();
        replay.At(index: 1438).Change24h.ShouldBeNull();
        replay.At(index: 1439).Change24h.ShouldNotBeNull();
        replay.At(index: 13_699).DistanceToHigh7d.ShouldNotBeNull();
        replay.At(index: 13_699).VolumeSpike.ShouldNotBeNull();
        replay.At(index: 13_699).VolumeSpike5m.ShouldNotBeNull();
        replay.At(index: 13_699).VolumeSpike15m.ShouldNotBeNull();
        replay.At(index: 253).Breakout4h.ShouldBeNull();
        replay.At(index: 254).Breakout4h.ShouldNotBeNull();
        replay.At(index: 254).Range4h.ShouldNotBeNull();
        replay.At(index: 13_699).Spread.ShouldBeNull();
        (replay.At(index: 13_699).Support ?? replay.At(index: 13_699).Resistance).ShouldNotBeNull();
    }

    [Test]
    public void RejectsAGap()
    {
        List<ScanCandle> minutes = RandomMinutes(count: 10, seed: 1);
        minutes.RemoveAt(index: 4);

        Should.Throw<ScanInputException>(() => new ScanReplay(minutes, listedAt: null));
    }

    private static List<ScanCandle> Sessions(List<ScanCandle> minutes)
    {
        List<ScanCandle> sessions = [];

        foreach (ScanCandle minute in minutes)
        {
            TimeSpan time = minute.OpenTime.TimeOfDay;
            bool open = minute.OpenTime.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && time >= TimeSpan.FromHours(4) &&
                time < new TimeSpan(hours: 20, minutes: 50, seconds: 0);
            decimal close = sessions.Count > 0 ? sessions[^1].Close : minute.Open;
            sessions.Add(open ? minute : new ScanCandle(minute.OpenTime, close, close, close, close, Volume: 0, Turnover: 0));
        }

        return sessions;
    }

    internal static List<ScanCandle> RandomMinutes(int count, int seed)
    {
        var random = new Random(seed);
        List<ScanCandle> minutes = [];
        var price = 100m;
        var quietLeft = 0;

        for (var index = 0; index < count; index++)
        {
            if (quietLeft == 0 && random.Next(maxValue: 400) == 0)
            {
                quietLeft = random.Next(minValue: 5, maxValue: 40);
            }

            DateTimeOffset openTime = Start + index * Minute;

            if (quietLeft > 0)
            {
                quietLeft--;
                minutes.Add(new ScanCandle(openTime, price, price, price, price, Volume: 0, Turnover: 0));

                continue;
            }

            decimal open = price;
            price = Math.Max(val1: 1m, Math.Round(price + (decimal)(random.NextDouble() - 0.495) * 0.8m, decimals: 4));
            decimal high = Math.Max(open, price) + Math.Round((decimal)random.NextDouble() * 0.3m, decimals: 4);
            decimal low = Math.Max(val1: 0.5m, Math.Min(open, price) - Math.Round((decimal)random.NextDouble() * 0.3m, decimals: 4));
            decimal volume = Math.Round((decimal)random.NextDouble() * 50m, decimals: 3) + 0.001m;
            minutes.Add(new ScanCandle(openTime, open, high, low, price, volume, Math.Round(volume * price, decimals: 6)));
        }

        return minutes;
    }
}