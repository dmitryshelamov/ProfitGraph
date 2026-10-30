using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     The trend metrics of the scanner's hours (ADR 0071, section 3): the price against the 50-hour EMA, the 20-hour EMA against it,
///     each seeded by the simple mean of its first closes, and Wilder's RSI of 14 hourly closes; each empty without enough hours.
/// </summary>
public sealed class ScanTrendTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void ThePriceAndTheFastAverageAreMeasuredAgainstTheSlowOne()
    {
        // Fifty hours at 100 seed both averages at 100; an hour closing at 151 moves the slow one by 2/51 and the fast one by 2/21 of 51.
        List<ScanCandle> hours = [.. Hours(Enumerable.Repeat(element: 100m, count: 50)), Hour(index: 50, close: 151m)];
        decimal slow = 100m + 2m / 51m * 51m;
        decimal fast = 100m + 2m / 21m * 51m;

        ScanMetrics metrics = ScanTrend.Apply(new ScanMetrics(), hours, price: 153m);

        metrics.TrendEma50.ShouldBe((153m - slow) / slow * 100m);
        metrics.EmaSpread20x50.ShouldBe((fast - slow) / slow * 100m);
    }

    [Test]
    public void FewerHoursThanAnAverageSpansLeaveItEmpty()
    {
        ScanMetrics metrics = ScanTrend.Apply(new ScanMetrics(), Hours(Enumerable.Repeat(element: 100m, count: 49)), price: 100m);

        (metrics.TrendEma50, metrics.EmaSpread20x50).ShouldBe((null, null));
        ScanTrend.Of(Hours(Enumerable.Repeat(element: 100m, count: 20))).Fast.ShouldBe(100m);
    }

    [Test]
    public void TheRsiNeedsFifteenClosesAndRunsFromNoGainsToNoLosses()
    {
        ScanTrend.Of(Hours(Enumerable.Range(start: 1, count: 14).Select(close => (decimal)close))).Rsi.ShouldBeNull();
        ScanTrend.Of(Hours(Enumerable.Range(start: 1, count: 15).Select(close => (decimal)close))).Rsi.ShouldBe(100m);
        ScanTrend.Of(Hours(Enumerable.Range(start: 1, count: 15).Select(close => 100m - close))).Rsi.ShouldBe(0m);
        ScanTrend.Of(Hours(Enumerable.Repeat(element: 100m, count: 15))).Rsi.ShouldBe(50m);
    }

    private static List<ScanCandle> Hours(IEnumerable<decimal> closes)
    {
        return [.. closes.Select((close, index) => Hour(index, close))];
    }

    private static ScanCandle Hour(int index, decimal close)
    {
        return new ScanCandle(Start.AddHours(index), close, close, close, close, Volume: 1, Turnover: 1);
    }
}