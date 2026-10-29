using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     Signals over history (ADR 0059, section 6): a pair signals when it enters the result, never at the first minute checked and at
///     most once per cooldown; it enters at the next open, and its outcomes run to later closes and to the next day's extremes.
/// </summary>
public sealed class ScanStudyTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Test]
    public void APairSignalsOnEnteringAtMostOncePerCooldown()
    {
        var replay = new ScanReplay(Rising(count: 600), listedAt: null);

        // Kept on minutes 10–19, 30–39 (within the cooldown of the first signal) and 80–89; a minute closes at 101 + its index.
        HashSet<int> kept = [.. new[] { 10, 30, 80 }.SelectMany(first => Enumerable.Range(first, count: 10))];

        IReadOnlyList<ScanSignal> signals = ScanStudy.Signals(
            "HYPEUSDT",
            replay,
            metrics => kept.Contains((int)metrics.Price!.Value - 101),
            replay.EndOf(index: 0),
            replay.EndOf(index: 599),
            TimeSpan.FromHours(1));

        signals.Select(signal => signal.At).ShouldBe([replay.EndOf(index: 10), replay.EndOf(index: 80)]);
    }

    [Test]
    public void TheFirstMinuteCheckedOnlyRecordsTheResult()
    {
        var replay = new ScanReplay(Rising(count: 100), listedAt: null);

        ScanStudy.Signals("HYPEUSDT", replay, _ => true, replay.EndOf(index: 0), replay.EndOf(index: 99), TimeSpan.FromHours(1)).ShouldBeEmpty();
    }

    [Test]
    public void OutcomesRunFromTheNextOpen()
    {
        // Each minute opens at 100 + index and closes one higher; minute 500 spikes to 5000 and dips to 50.
        List<ScanCandle> minutes = Rising(count: 2000);
        minutes[500] = minutes[500] with { High = 5000, Low = 50 };
        var replay = new ScanReplay(minutes, listedAt: null);
        var calls = 0;

        ScanSignal signal = ScanStudy.Signals("HYPEUSDT", replay, _ => calls++ == 1, replay.EndOf(index: 99), replay.EndOf(index: 1999), TimeSpan.FromHours(1))
            .ShouldHaveSingleItem();

        signal.At.ShouldBe(replay.EndOf(index: 100));
        signal.EntryPrice.ShouldBe(201m);
        signal.Change15m.ShouldBe((216m - 201m) / 201m * 100m);
        signal.Change24h.ShouldBe((1641m - 201m) / 201m * 100m);
        signal.MaxRise24h.ShouldBe((5000m - 201m) / 201m * 100m);
        signal.MaxDrop24h.ShouldBe((50m - 201m) / 201m * 100m);
    }

    [Test]
    public void AHorizonTheHistoryDoesNotReachIsEmpty()
    {
        var replay = new ScanReplay(Rising(count: 100), listedAt: null);
        var calls = 0;

        ScanSignal signal = ScanStudy.Signals("HYPEUSDT", replay, _ => calls++ == 1, replay.EndOf(index: 50), replay.EndOf(index: 99), TimeSpan.FromHours(1))
            .ShouldHaveSingleItem();

        signal.Change15m.ShouldNotBeNull();
        (signal.Change1h, signal.Change4h, signal.MaxRise24h).ShouldBe(((decimal?)null, (decimal?)null, (decimal?)null));
    }

    [Test]
    public void SummaryCountsTheShareAboveZeroTheMeanAndTheMedian()
    {
        ScanSignal[] signals = [Signal(change1h: 2), Signal(change1h: -1), Signal(change1h: 5), Signal(change1h: null)];

        ScanStudySummary summary = ScanStudy.Summarize(signals);

        summary.Signals.ShouldBe(4);
        summary.Change1h.ShouldBe(new ScanOutcomeSummary(Count: 3, 2m / 3m, Mean: 2m, Median: 2m));
        summary.Change24h.ShouldBe(new ScanOutcomeSummary(Count: 0, RisingShare: null, Mean: null, Median: null));
    }

    private static ScanSignal Signal(decimal? change1h)
    {
        return new ScanSignal(
            "X",
            Start,
            EntryPrice: 1,
            new ScanMetrics(),
            Change15m: null,
            change1h,
            Change4h: null,
            Change24h: null,
            MaxRise24h: null,
            MaxDrop24h: null);
    }

    private static List<ScanCandle> Rising(int count)
    {
        return
        [
            .. Enumerable.Range(start: 0, count)
                .Select(index => new ScanCandle(Start + index * Minute, 100 + index, 101 + index, 100 + index, 101 + index, Volume: 1, Turnover: 1))
        ];
    }
}