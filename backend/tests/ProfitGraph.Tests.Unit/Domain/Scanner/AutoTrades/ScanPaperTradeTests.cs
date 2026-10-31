using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Domain.Scanner.AutoTrades;

/// <summary>
///     Paper trades and requests (ADR 0071, sections 6 and 7): a paper trade steps through the minutes after its snapshot by the rules
///     the backtest follows cards with, once each, and its result is in R and in quote; a request is handled once; the day's tally counts
///     open, created and lost trades.
/// </summary>
public sealed class ScanPaperTradeTests
{
    private static readonly DateTimeOffset Snapshot = new DateTimeOffset(year: 2026, month: 10, day: 31, hour: 12, minute: 0, second: 5, TimeSpan.Zero);
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 10, day: 31, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanTradeCosts Costs = new ScanTradeCosts(limitFeeRate: 0.001m, marketFeeRate: 0.002m, spread: 0.0005m);

    private static readonly ScanTradeIdea Idea =
        new ScanTradeIdea(ScanScenario.Target2R, Entry: 100m, Stop: 98m, Target: 104m, RewardRisk: 2m, ScanTradeIdeas.MarketEntryWait);

    [Test]
    public void APaperTradeStepsFromTheMinuteOfItsSnapshotAsTheBacktestDoes()
    {
        ScanPaperTrade trade = Trade();
        List<ScanCandle> minutes = Flat(Minute.AddMinutes(-2), count: 8);
        (trade.ExpectedR, trade.Chance).ShouldBe((0.12m, 0.4m));

        // The two minutes before the snapshot's are not stepped; the entry fills in the snapshot's minute and the target four minutes on.
        minutes[2] = minutes[2] with { Low = 99.9m };
        minutes[6] = minutes[6] with { High = 104.1m };

        trade.Advance(minutes.Take(4)).ShouldBeFalse();
        trade.Progress.EnteredAt.ShouldBe(Minute);
        trade.Advance(minutes).ShouldBeTrue();

        ScanCardOutcome expected = ScanCardFollow.Follow(minutes, index: 1, Idea, Costs)!;
        trade.Progress.Outcome.ShouldBe(expected);
        (trade.IsOpen, trade.Pnl).ShouldBe((false, expected.ResultR * 5m));
        trade.NextMinute.ShouldBe(Minute.AddMinutes(5));
    }

    [Test]
    public void AMinuteIsSteppedOnceAndAClosedTradeStaysClosed()
    {
        ScanPaperTrade trade = Trade();
        List<ScanCandle> minutes = Flat(Minute, count: 3);
        minutes[0] = minutes[0] with { Low = 97m, Close = 97.5m };

        trade.Advance(minutes).ShouldBeTrue();
        ScanCardProgress closed = trade.Progress;
        trade.Advance(minutes).ShouldBeFalse();

        trade.Progress.ShouldBe(closed);
        closed.Outcome!.Exit.ShouldBe(ScanTradeExit.Stop);
    }

    [Test]
    public void ATallyCountsOpenTradesThoseOfTheDayAndTheirNetLoss()
    {
        DateTimeOffset dayStart = ScanAutoTradeTally.DayStart(Snapshot);
        ScanPaperTrade open = Trade();
        ScanPaperTrade stopped = Trade();
        List<ScanCandle> minutes = Flat(Minute, count: 3);
        minutes[0] = minutes[0] with { Low = 97m, Close = 97.5m };
        stopped.Advance(minutes);

        ScanAutoTradeTally tally = ScanAutoTradeTally.Of([open, stopped], dayStart);

        (dayStart, tally.Open, tally.Today).ShouldBe((Minute.AddHours(-12), 1, 2));
        tally.Loss.ShouldBe(-stopped.Pnl!.Value);
    }

    [Test]
    public void ARequestIsHandledOnce()
    {
        var request = ScanAutoTradeRequest.Create(ScanAutoTradeRuleId.New(), "SOLUSDT", Snapshot, price: 100m, Snapshot);
        var paper = ScanPaperTradeId.New();

        request.TookOnPaper(paper, Snapshot.AddSeconds(3));

        (request.Status, request.PaperTradeId, request.HandledAt).ShouldBe((ScanAutoTradeRequestStatus.Paper, paper, Snapshot.AddSeconds(3)));
        Should.Throw<ScanAutoTradeRuleException>(() => request.Skipped(ScanAutoTradeSkip.NoIdea, Snapshot.AddSeconds(4)));

        var skipped = ScanAutoTradeRequest.Create(ScanAutoTradeRuleId.New(), "SOLUSDT", Snapshot, price: null, Snapshot);
        skipped.Skipped(ScanAutoTradeSkip.Refused, Snapshot, "scan_trade_live");
        (skipped.Status, skipped.Skip, skipped.ErrorCode).ShouldBe((ScanAutoTradeRequestStatus.Skipped, ScanAutoTradeSkip.Refused, "scan_trade_live"));
    }

    private static ScanPaperTrade Trade()
    {
        return ScanPaperTrade.Open(
            ScanAutoTradeRuleId.New(),
            new VenueId("bybit"),
            "SOLUSDT",
            "1h",
            Idea,
            amount: 212.76m,
            risk: 5m,
            Costs,
            expectedR: 0.12m,
            chance: 0.4m,
            Snapshot,
            Snapshot.AddSeconds(2));
    }

    private static List<ScanCandle> Flat(DateTimeOffset start, int count)
    {
        return
        [
            .. Enumerable.Range(start: 0, count)
                .Select(index => new ScanCandle(start.AddMinutes(index), Open: 100m, High: 100m, Low: 100m, Close: 100m, Volume: 1, Turnover: 1))
        ];
    }
}