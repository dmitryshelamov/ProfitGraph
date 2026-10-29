using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

public sealed class ScanMetricCalculatorTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Test]
    public void ChangeRunsFromTheFirstOpenToTheLastCloseOfItsWindow()
    {
        List<ScanCandle> minutes = Series(Start, Minute, count: 15, index => Bar(Start, 100 + index, 101 + index));

        ScanMetrics metrics = Calculate(minutes);

        metrics.Change5m.ShouldBe((115m - 110m) / 110m * 100m);
        metrics.Change15m.ShouldBe((115m - 100m) / 100m * 100m);
        metrics.Change1h.ShouldBeNull();
        metrics.Change24h.ShouldBeNull();
    }

    [Test]
    public void TurnoverSumsTheWindowAndIsEmptyWithoutFullHistory()
    {
        List<ScanCandle> minutes = Series(Start, Minute, count: 60, index => Flat(Start, price: 100, index + 1));

        ScanMetrics metrics = Calculate(minutes);

        metrics.Turnover1h.ShouldBe(1830m);
        metrics.Turnover24h.ShouldBeNull();
    }

    [Test]
    public void VolumeSpikeDividesTheLastHourByTheMedianOfEarlierClosedHours()
    {
        DateTimeOffset windowStart = Start + 100 * Hour;
        List<ScanCandle> hours = Series(Start, Hour, count: 100, index => Flat(Start, price: 100, index + 1));

        // The hour that overlaps the last 60 minutes is not part of the baseline.
        hours.Add(Flat(windowStart, price: 100, turnover: 1_000_000));
        List<ScanCandle> minutes = Series(windowStart, Minute, count: 60, _ => Flat(windowStart, price: 100, turnover: 1));

        Calculate(minutes, hours).VolumeSpike.ShouldBe(60m / 50.5m);
    }

    [Test]
    public void VolumeSpikeLeavesHoursWithoutTradesOutOfTheBaseline()
    {
        // A market closed every other hour (a night, a weekend) would make the median of all hours zero (ADR 0062).
        DateTimeOffset windowStart = Start + 167 * Hour;
        List<ScanCandle> hours = Series(Start, Hour, count: 167, index => Flat(Start, price: 100, index % 2 == 0 ? 0 : 10));
        List<ScanCandle> minutes = Series(windowStart, Minute, count: 60, _ => Flat(windowStart, price: 100, turnover: 1));

        Calculate(minutes, hours).VolumeSpike.ShouldBe(6m);
        Calculate(minutes, hours[^143..]).VolumeSpike.ShouldBeNull();
    }

    [Test]
    public void VolumeSpikeIsEmptyBelowSeventyTwoHoursOrOnAZeroMedian()
    {
        DateTimeOffset windowStart = Start + 71 * Hour;
        List<ScanCandle> minutes = Series(windowStart, Minute, count: 60, _ => Flat(windowStart, price: 100, turnover: 1));

        Calculate(minutes, Series(Start, Hour, count: 71, _ => Flat(Start, price: 100, turnover: 5))).VolumeSpike.ShouldBeNull();

        DateTimeOffset laterStart = Start + 72 * Hour;
        List<ScanCandle> laterMinutes = Series(laterStart, Minute, count: 60, _ => Flat(laterStart, price: 100, turnover: 1));

        Calculate(laterMinutes, Series(Start, Hour, count: 72, _ => Flat(Start, price: 100, turnover: 0))).VolumeSpike.ShouldBeNull();
        Calculate(laterMinutes, Series(Start, Hour, count: 72, _ => Flat(Start, price: 100, turnover: 5))).VolumeSpike.ShouldBe(12m);
    }

    [Test]
    public void NatrAveragesTrueRangeOverCloseAndCountsTheGapFromThePreviousClose()
    {
        List<ScanCandle> hours = Series(Start, Hour, count: 25, index => Flat(Start, index == 24 ? 120 : 100, turnover: 1));

        Calculate([], hours).Natr24h.ShouldBe(20m / 120m / 24m * 100m);
        Calculate([], hours[1..]).Natr24h.ShouldBeNull();
    }

    [Test]
    public void NatrOfRangingHoursIsTheirRangeOverClose()
    {
        List<ScanCandle> hours = Series(Start, Hour, count: 25, _ => Bar(Start, open: 100, close: 100, high: 110, low: 90));

        Calculate([], hours).Natr24h.ShouldBe(20m);
    }

    [TestCase(arg1: 1, arg2: 1)]
    [TestCase(arg1: 0, arg2: 0)]
    public void EfficiencyRatioIsOneForAStraightTrendAndZeroWithoutMovement(int step, int expected)
    {
        List<ScanCandle> hours = Series(Start, Hour, count: 25, index => Flat(Start, 100 + step * index, turnover: 1));

        Calculate([], hours).EfficiencyRatio24h.ShouldBe(expected);
    }

    [Test]
    public void EfficiencyRatioIsNetMoveOverPath()
    {
        // Twelve pairs of +2 and −1: net 12, path 36.
        List<ScanCandle> hours = [];
        decimal close = 100;

        for (var index = 0; index < 25; index++)
        {
            if (index > 0)
            {
                close += index % 2 == 1 ? 2 : -1;
            }

            hours.Add(Flat(Start + index * Hour, close, turnover: 1));
        }

        Calculate([], hours).EfficiencyRatio24h.ShouldBe(12m / 36m);
        Calculate([], hours[1..]).EfficiencyRatio24h.ShouldBeNull();
    }

    [Test]
    public void DistancesOverADayUseTheMinutesHighAndLow()
    {
        List<ScanCandle> minutes = Series(
            Start,
            Minute,
            count: 1440,
            index => index switch
            {
                10 => Bar(Start, open: 100, close: 100, high: 120, low: 100),
                20 => Bar(Start, open: 100, close: 100, high: 100, low: 80),
                _ => Flat(Start, price: 100, turnover: 1)
            });

        ScanMetrics metrics = Calculate(minutes);

        metrics.DistanceToHigh24h.ShouldBe((100m - 120m) / 120m * 100m);
        metrics.DistanceToLow24h.ShouldBe(25m);
        Calculate(minutes[1..]).DistanceToHigh24h.ShouldBeNull();
    }

    [Test]
    public void DistancesOverAWeekUseTheHoursAndTheMinutesAfterTheLastHour()
    {
        List<ScanCandle> hours = Series(
            Start,
            Hour,
            count: 168,
            index => index == 5 ? Bar(Start, open: 100, close: 100, high: 150, low: 100) : Flat(Start, price: 100, turnover: 1));

        DateTimeOffset afterHours = Start + 168 * Hour;

        List<ScanCandle> minutes = Series(
            afterHours,
            Minute,
            count: 10,
            index => index == 3 ? Bar(afterHours, open: 100, close: 100, high: 100, low: 50) : Flat(afterHours, price: 100, turnover: 1));

        ScanMetrics metrics = Calculate(minutes, hours);

        metrics.DistanceToHigh7d.ShouldBe((100m - 150m) / 150m * 100m);
        metrics.DistanceToLow7d.ShouldBe(100m);
        Calculate(minutes, hours[1..]).DistanceToLow7d.ShouldBeNull();
    }

    [Test]
    public void SpreadIsTheGapOverTheMid()
    {
        Calculate([], [], bid: 99, ask: 101).Spread.ShouldBe(2m);
        Calculate([], [], bid: 101, ask: 99).Spread.ShouldBeNull();
        Calculate([], [], bid: 0, ask: 99).Spread.ShouldBeNull();
        Calculate([], [], bid: null, ask: 99).Spread.ShouldBeNull();
    }

    [Test]
    public void PriceIsTheFormingPriceOrTheLastClose()
    {
        List<ScanCandle> minutes = [Flat(Start, price: 100, turnover: 1)];

        Calculate(minutes, [], lastPrice: 105).Price.ShouldBe(105m);
        Calculate(minutes).Price.ShouldBe(100m);
        Calculate([]).Price.ShouldBeNull();
    }

    [Test]
    public void ListingAgeCountsWholeDaysAndNeverGoesBelowZero()
    {
        ScanMetricCalculator.Calculate(Input(Start, Start + TimeSpan.FromHours(95))).ListingAgeDays.ShouldBe(3);
        ScanMetricCalculator.Calculate(Input(Start + Hour, Start)).ListingAgeDays.ShouldBe(0);
        ScanMetricCalculator.Calculate(Input(listedAt: null, Start)).ListingAgeDays.ShouldBeNull();
    }

    [Test]
    public void StaleAfterFifteenMinutesWithoutTrades()
    {
        List<ScanCandle> minutes = Series(Start, Minute, count: 20, index => Flat(Start, price: 100, index <= 4 ? 1 : 0));

        // The last traded minute ends at 00:05.
        Calculate(minutes, [], now: Start + 20 * Minute).Stale.ShouldBeFalse();
        Calculate(minutes, [], now: Start + 20 * Minute + TimeSpan.FromSeconds(1)).Stale.ShouldBeTrue();
        Calculate(Series(Start, Minute, count: 3, _ => Flat(Start, price: 100, turnover: 0))).Stale.ShouldBeTrue();
    }

    [Test]
    public void RejectsAGapAMisalignedAndANonUtcCandle()
    {
        List<ScanCandle> gap = [Flat(Start, price: 100, turnover: 1), Flat(Start + 2 * Minute, price: 100, turnover: 1)];

        Should.Throw<ScanInputException>(() => Calculate(gap));
        Should.Throw<ScanInputException>(() => Calculate([Flat(Start + TimeSpan.FromSeconds(30), price: 100, turnover: 1)]));
        Should.Throw<ScanInputException>(() => Calculate([], [Flat(Start + Minute, price: 100, turnover: 1)]));
        Should.Throw<ScanInputException>(() => Calculate([Flat(Start.ToOffset(TimeSpan.FromHours(3)), price: 100, turnover: 1)]));
    }

    private static ScanMetrics Calculate(
        List<ScanCandle> minutes,
        List<ScanCandle>? hours = null,
        decimal? lastPrice = null,
        decimal? bid = null,
        decimal? ask = null,
        DateTimeOffset? now = null)
    {
        return ScanMetricCalculator.Calculate(new ScanInput(minutes, hours ?? [], lastPrice, bid, ask, ListedAt: null, now ?? Start));
    }

    private static ScanInput Input(DateTimeOffset? listedAt, DateTimeOffset now)
    {
        return new ScanInput([], [], LastPrice: null, BestBid: null, BestAsk: null, listedAt, now);
    }

    private static List<ScanCandle> Series(DateTimeOffset start, TimeSpan step, int count, Func<int, ScanCandle> candle)
    {
        return Enumerable.Range(start: 0, count).Select(index => candle(index) with { OpenTime = start + index * step }).ToList();
    }

    private static ScanCandle Flat(DateTimeOffset openTime, decimal price, decimal turnover)
    {
        decimal volume = turnover == 0 ? 0 : 1;

        return new ScanCandle(openTime, price, price, price, price, volume, turnover);
    }

    private static ScanCandle Bar(DateTimeOffset openTime, decimal open, decimal close, decimal? high = null, decimal? low = null)
    {
        return new ScanCandle(
            openTime,
            open,
            high ?? Math.Max(open, close),
            low ?? Math.Min(open, close),
            close,
            Volume: 1,
            Turnover: 1);
    }
}