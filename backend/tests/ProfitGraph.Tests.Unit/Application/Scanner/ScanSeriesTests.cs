using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Runtime;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Application.Scanner;

public sealed class ScanSeriesTests
{
    private static readonly DateTimeOffset Hour0 = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 10, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Test]
    public void IsHiddenUntilItsHistoryIsMerged()
    {
        var series = new ScanSeries("BTCUSDT");

        series.Accept(Closed(Hour0, close: 101));

        series.IsWarm.ShouldBeFalse();
        series.Input(Hour0, quote: null).ShouldBeNull();
    }

    [Test]
    public void MergesHeldStreamCandlesAfterTheRestHistory()
    {
        var series = new ScanSeries("BTCUSDT");
        series.Accept(Closed(Hour0 + 3 * Minute, close: 104));

        series.Merge([Candle(Hour0, close: 101), Candle(Hour0 + Minute, close: 102), Candle(Hour0 + 2 * Minute, close: 103)], [], listedAt: null);

        ScanInput input = series.Input(Hour0 + 4 * Minute, quote: null)!;
        input.Minutes.Select(minute => minute.Close).ShouldBe([101m, 102m, 103m, 104m]);
        series.IsWarm.ShouldBeTrue();
    }

    [Test]
    public void FillsHolesTheVenueReportedNowhereWithFlatCandles()
    {
        var series = new ScanSeries("BTCUSDT");

        series.Merge([Candle(Hour0, close: 101), Candle(Hour0 + 3 * Minute, close: 104)], [], listedAt: null);

        ScanInput input = series.Input(Hour0 + 4 * Minute, quote: null)!;
        input.Minutes.Select(minute => minute.OpenTime).ShouldBe([Hour0, Hour0 + Minute, Hour0 + 2 * Minute, Hour0 + 3 * Minute]);
        input.Minutes[1].ShouldBe(new ScanCandle(Hour0 + Minute, Open: 101, High: 101, Low: 101, Close: 101, Volume: 0, Turnover: 0));
    }

    [Test]
    public void AMinuteTheStreamSkipsIsFlatAndARealCandleReplacesIt()
    {
        var series = new ScanSeries("BTCUSDT");
        series.Merge([Candle(Hour0, close: 101)], [], listedAt: null);

        series.Accept(Closed(Hour0 + 2 * Minute, close: 103));
        series.Accept(Closed(Hour0 + Minute, close: 102));

        series.Input(Hour0 + 3 * Minute, quote: null)!.Minutes.Select(minute => minute.Close).ShouldBe([101m, 102m, 103m]);
    }

    [Test]
    public void AdvancesFlatToTheLastClosedMinuteWhileConnected()
    {
        var series = new ScanSeries("BTCUSDT");
        series.Merge([Candle(Hour0, close: 101)], [], listedAt: null);

        series.AdvanceTo(Hour0 + 3 * Minute);

        series.Input(Hour0 + 3 * Minute, quote: null)!.Minutes.Select(minute => minute.OpenTime).ShouldBe([Hour0, Hour0 + Minute, Hour0 + 2 * Minute]);
    }

    [Test]
    public void AggregatesAClosedHourFromItsMinutes()
    {
        var series = new ScanSeries("BTCUSDT");
        series.Merge([], [Candle(Hour0 - TimeSpan.FromHours(1), close: 100)], listedAt: null);

        for (var index = 0; index < 60; index++)
        {
            series.Accept(Closed(Hour0 + index * Minute, 100 + index, turnover: 2));
        }

        // The first minute after the merge starts the minute series; the next ones follow it.
        ScanInput input = series.Input(Hour0 + TimeSpan.FromHours(1), quote: null)!;
        input.Hours.Count.ShouldBe(2);
        input.Hours[1].ShouldBe(new ScanCandle(Hour0, Open: 100, High: 159, Low: 100, Close: 159, Volume: 60, Turnover: 120));
    }

    [Test]
    public void AReconnectHoldsCandlesUntilTheBackfillClosesTheGap()
    {
        var series = new ScanSeries("BTCUSDT");
        series.Merge([Candle(Hour0, close: 101)], [Candle(Hour0 - TimeSpan.FromHours(1), close: 100)], listedAt: null);

        series.MarkGap();
        series.Accept(Closed(Hour0 + 3 * Minute, close: 104));

        series.HasGap.ShouldBeTrue();
        series.BackfillFrom.ShouldBe((Hour0 + Minute, Hour0));
        series.Input(Hour0 + 4 * Minute, quote: null)!.Minutes.Count.ShouldBe(1);

        series.Merge([Candle(Hour0 + Minute, close: 102), Candle(Hour0 + 2 * Minute, close: 103)], [], listedAt: null);

        series.HasGap.ShouldBeFalse();
        series.Input(Hour0 + 4 * Minute, quote: null)!.Minutes.Select(minute => minute.Close).ShouldBe([101m, 102m, 103m, 104m]);
    }

    [Test]
    public void KeepsOnlyTheLastDayOfMinutes()
    {
        var series = new ScanSeries("BTCUSDT");
        ScanCandle[] minutes = [.. Enumerable.Range(start: 0, count: 1500).Select(index => Candle(Hour0 + index * Minute, close: 100))];

        series.Merge(minutes, [], listedAt: null);
        series.Accept(Closed(Hour0 + 1500 * Minute, close: 100));

        ScanInput input = series.Input(Hour0 + 1501 * Minute, quote: null)!;
        input.Minutes.Count.ShouldBe(ScanSeries.MinuteCapacity);
        input.Minutes[^1].OpenTime.ShouldBe(Hour0 + 1500 * Minute);
    }

    [Test]
    public void InputCarriesTheFormingPriceTheQuoteAndTheListingTime()
    {
        var series = new ScanSeries("BTCUSDT");
        series.Merge([Candle(Hour0, close: 101)], [], Hour0);

        series.Accept(new ScanCandleUpdateInternal("BTCUSDT", Candle(Hour0 + Minute, close: 107), Closed: false));
        ScanInput input = series.Input(Hour0 + Minute, new ScanQuoteInternal("BTCUSDT", BestBid: 99, BestAsk: 101))!;

        input.LastPrice.ShouldBe(107m);
        input.BestBid.ShouldBe(99m);
        input.BestAsk.ShouldBe(101m);
        input.ListedAt.ShouldBe(Hour0);
    }

    private static ScanCandleUpdateInternal Closed(DateTimeOffset openTime, decimal close, decimal turnover = 1)
    {
        return new ScanCandleUpdateInternal("BTCUSDT", Candle(openTime, close, turnover), Closed: true);
    }

    private static ScanCandle Candle(DateTimeOffset openTime, decimal close, decimal turnover = 1)
    {
        return new ScanCandle(openTime, close, close, close, close, Volume: 1, turnover);
    }
}