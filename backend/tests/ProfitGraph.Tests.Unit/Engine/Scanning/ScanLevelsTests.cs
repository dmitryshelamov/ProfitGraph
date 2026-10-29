using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     Support and resistance (ADR 0059, section 8): swing highs and lows with two hours on each side and the hours' extremes, prices
///     within 0.5 % merged into their mean, the nearest levels more than 0.1 % from the price, and the reward-to-risk of a long.
/// </summary>
public sealed class ScanLevelsTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void SwingsAndExtremesBecomeLevelsAndNearPricesMerge()
    {
        // Flat at 100–101 with a swing high at 110 (hour 10), a second one at 110.4 (hour 20) and a swing low at 90 (hour 15).
        List<ScanCandle> hours = Hours(
            count: 30,
            index => index switch
            {
                10 => (110m, 100m),
                15 => (101m, 90m),
                20 => (110.4m, 100m),
                _ => (101m, 100m)
            });

        IReadOnlyList<decimal> levels = ScanLevels.Of(hours);

        // 110.4 is also the hours' highest high, so it counts twice in its cluster; 90 is a swing low and the lowest low.
        levels.ShouldBe([90m, (110m + 110.4m + 110.4m) / 3m]);
    }

    [Test]
    public void FewerThanADayOfHoursGivesNoLevels()
    {
        ScanLevels.Of(Hours(count: 23, _ => (101m, 100m))).ShouldBeEmpty();
    }

    [Test]
    public void TheNearestLevelsLieOutsideTheClearance()
    {
        ScanMetrics metrics = ScanLevels.Apply(new ScanMetrics(), [90m, 99.95m, 100.05m, 104m, 120m], price: 100m);

        (metrics.Support, metrics.Resistance).ShouldBe(((decimal?)90m, (decimal?)104m));
        metrics.DistanceToSupport.ShouldBe(-10m);
        metrics.DistanceToResistance.ShouldBe(4m);
        metrics.RewardRisk.ShouldBe(0.4m);
    }

    [Test]
    public void APriceAboveEveryLevelHasNoResistanceAndNoRatio()
    {
        ScanMetrics metrics = ScanLevels.Apply(new ScanMetrics(), [90m, 95m], price: 100m);

        (metrics.Support, metrics.Resistance, metrics.RewardRisk).ShouldBe(((decimal?)95m, (decimal?)null, (decimal?)null));
    }

    private static List<ScanCandle> Hours(int count, Func<int, (decimal High, decimal Low)> range)
    {
        return
        [
            .. Enumerable.Range(start: 0, count).Select(index =>
            {
                (decimal high, decimal low) = range(index);

                return new ScanCandle(Start.AddHours(index), low, high, low, low, Volume: 1, Turnover: 1);
            })
        ];
    }
}
