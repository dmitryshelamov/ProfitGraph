using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.TInvest.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     Completing a share's history from a reference share (ADR 0023): open intervals, admitted segments, candles without
///     trades at the last real close, and closures for nights, weekends and segments the share did not trade in.
/// </summary>
public sealed class TInvestSessionGridTests
{
    private static readonly Timeframe Hour = Timeframe.Parse("1h");

    // Monday 2026-10-26 00:00 UTC; Moscow is UTC+3.
    private static readonly DateTimeOffset Monday = new DateTimeOffset(year: 2026, month: 10, day: 26, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void AQuietHourInsideATradedSegmentHasNoTrades_AndTheNightIsClosed()
    {
        HistoricalCandlesInternal history = Build(
            from: 6,
            to: 30,
            [6, 7, 8, 9, 30, 31],
            [(6, 101m), (8, 103m), (30, 110m)]);

        history.Candles.Select(candle => (Hours(candle.OpenTime), candle.NoTrades, candle.Close))
            .ShouldBe([(6, false, 101m), (7, true, 101m), (8, false, 103m), (9, true, 103m)]);
        history.Candles.Where(candle => candle.NoTrades).ShouldAllBe(candle => candle.Volume == 0m && candle.Open == candle.High && candle.Low == candle.Close);
        history.Closures.ShouldBe([new CandleRangeInternal(At(10), At(30))]);
    }

    [Test]
    public void AReferenceHoleSplitsSegments_AndASegmentTheShareNeverTradedInIsClosedForIt()
    {
        HistoricalCandlesInternal history = Build(from: 6, to: 12, [6, 7, 8, 9, 10, 11], [(6, 100m), (7, 100m)], gapsAt: [8]);

        history.Candles.Select(candle => Hours(candle.OpenTime)).ShouldBe([6, 7]);
        history.Closures.ShouldBe([new CandleRangeInternal(At(8), At(12))]);
    }

    [Test]
    public void ATradeOfTheShareWhereTheReferenceIsSilent_OpensTheInterval()
    {
        HistoricalCandlesInternal history = Build(from: 6, to: 9, [6, 8], [(6, 100m), (7, 102m), (8, 101m)]);

        history.Candles.Select(candle => (Hours(candle.OpenTime), candle.NoTrades)).ShouldBe([(6, false), (7, false), (8, false)]);
        history.Closures.ShouldBeEmpty();
    }

    [Test]
    public void TheFirstQuietIntervals_UseTheCloseBeforeTheRange_OrCloseWithoutOne()
    {
        HistoricalCandlesInternal seeded = Build(from: 6, to: 9, [6, 7, 8], [(8, 105m)], priorClose: 99m);
        HistoricalCandlesInternal unseeded = Build(from: 6, to: 9, [6, 7, 8], [(8, 105m)]);

        seeded.Candles.Select(candle => (Hours(candle.OpenTime), candle.Close, candle.NoTrades)).ShouldBe([(6, 99m, true), (7, 99m, true), (8, 105m, false)]);
        unseeded.Candles.Select(candle => Hours(candle.OpenTime)).ShouldBe([8]);
        unseeded.Closures.ShouldBe([new CandleRangeInternal(At(6), At(8))]);
    }

    [Test]
    public void ATradeAfterTheRangeAdmitsItsSegment()
    {
        HistoricalCandlesInternal history = Build(from: 6, to: 8, [6, 7, 8, 9], [(9, 104m)], paddedTo: 12, priorClose: 100m);

        history.Candles.Select(candle => (Hours(candle.OpenTime), candle.NoTrades)).ShouldBe([(6, true), (7, true)]);
    }

    [Test]
    public void WeekendIntervalsAreClosures_AndTheirTradesAreLeftOut()
    {
        // Friday 12:00 UTC is hour 108; Saturday begins in Moscow at Friday 21:00 UTC, hour 117.
        HistoricalCandlesInternal history = Build(from: 108, to: 130, [108, 125, 126], [(108, 100m), (125, 120m), (126, 121m)]);

        TInvestSessionGrid.IsWeekend(At(116)).ShouldBeFalse();
        TInvestSessionGrid.IsWeekend(At(117)).ShouldBeTrue();
        history.Candles.Select(candle => Hours(candle.OpenTime)).ShouldBe([108]);
        history.Closures.ShouldBe([new CandleRangeInternal(At(109), At(130))]);
    }

    private static HistoricalCandlesInternal Build(
        int from,
        int to,
        int[] reference,
        (int Hour, decimal Close)[] share,
        decimal? priorClose = null,
        int? paddedTo = null,
        int[]? gapsAt = null)
    {
        IEnumerable<int> referenceHours = reference.Except(gapsAt ?? []);
        Dictionary<DateTimeOffset, Candle> candles = share.ToDictionary(
            item => At(item.Hour),
            item => new Candle(At(item.Hour), item.Close, item.Close, item.Close, item.Close, Volume: 10m));

        return TInvestSessionGrid.Build(Hour, At(from), At(to), At(from), At(paddedTo ?? to), candles, referenceHours.Select(At).ToHashSet(), priorClose);
    }

    private static DateTimeOffset At(int hours)
    {
        return Monday.AddHours(hours);
    }

    private static int Hours(DateTimeOffset time)
    {
        return (int)(time - Monday).TotalHours;
    }
}