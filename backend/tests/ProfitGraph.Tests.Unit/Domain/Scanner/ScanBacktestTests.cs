using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Domain.Scanner;

/// <summary>
///     A scanner backtest (ADR 0059, section 6): it starts without a result, is completed once, keeps at most 2,000 signals, and its
///     period ends after it starts.
/// </summary>
public sealed class ScanBacktestTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanStudySummary EmptySummary = ScanStudy.Summarize([]);

    [Test]
    public void StartsWithoutAResult()
    {
        ScanBacktest backtest = Backtest();

        (backtest.Result, backtest.CompletedAt).ShouldBe(((ScanBacktestResult?)null, (DateTimeOffset?)null));
        (backtest.From, backtest.To).ShouldBe((Now.AddDays(-30), Now));
    }

    [Test]
    public void CompletesOnce()
    {
        ScanBacktest backtest = Backtest();
        var first = new ScanBacktestResult(Pairs: 3, ["BADUSDT"], EmptySummary, [], SignalsTruncated: false);

        backtest.Complete(first, Now.AddMinutes(15)).ShouldBeTrue();
        backtest.Complete(first with { Pairs = 4 }, Now.AddMinutes(16)).ShouldBeFalse();

        backtest.Result.ShouldBe(first);
        backtest.CompletedAt.ShouldBe(Now.AddMinutes(15));
    }

    [Test]
    public void RejectsMoreSignalsThanItKeeps()
    {
        var signal = new ScanSignal(
            "X",
            Now,
            EntryPrice: 1,
            new ScanMetrics(),
            Change15m: null,
            Change1h: null,
            Change4h: null,
            Change24h: null,
            MaxRise24h: null,
            MaxDrop24h: null,
            Trades: null);
        var result = new ScanBacktestResult(Pairs: 1, [], EmptySummary, [.. Enumerable.Repeat(signal, ScanBacktest.MaxStoredSignals + 1)], SignalsTruncated: true);

        Should.Throw<ArgumentException>(() => Backtest().Complete(result, Now));
    }

    [Test]
    public void APeriodEndsAfterItStarts()
    {
        Should.Throw<ArgumentException>(() => ScanBacktest.Start(
            UserId.New(),
            new VenueId("bybit"),
            new ScanPresetName("Импульс"),
            new ScanFilter([], HideStale: true),
            Now,
            Now,
            BackgroundJobId.New(),
            Now));
    }

    [Test]
    public void AFilterSkipsConditionsOnUnknownMetrics()
    {
        var filter = new ScanFilter([new ScanCondition(ScanMetric.Spread, minimum: null, maximum: 0.1m)], HideStale: false);

        filter.Keeps(new ScanMetrics()).ShouldBeFalse();
        filter.Keeps(new ScanMetrics(), new HashSet<ScanMetric> { ScanMetric.Spread }).ShouldBeTrue();
    }

    private static ScanBacktest Backtest()
    {
        return ScanBacktest.Start(
            UserId.New(),
            new VenueId("bybit"),
            new ScanPresetName("Импульс"),
            new ScanFilter([new ScanCondition(ScanMetric.Change1h, minimum: 3, maximum: null)], HideStale: true),
            Now.AddDays(-30),
            Now,
            BackgroundJobId.New(),
            Now);
    }
}