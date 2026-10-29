using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

using ScanTrade = ProfitGraph.Domain.Scanner.Trades.ScanTrade;

namespace ProfitGraph.Tests.Unit.Domain.Scanner.Trades;

/// <summary>
///     A scanner trade (ADR 0060) follows its bot: it waits, opens on the entry's fill and closes by its exit; a waiting trade expires,
///     is cancelled or fails; only a finished trade releases its bot.
/// </summary>
public sealed class ScanTradeTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 30, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void StartsWaitingAndExpiresAfterItsValidity()
    {
        ScanTrade trade = Trade();

        trade.Status.ShouldBe(ScanTradeStatus.Waiting);
        trade.IsLive.ShouldBeTrue();
        trade.ExpiresAt.ShouldBe(Now.AddHours(1));
        trade.IsOverdue(Now.AddMinutes(59)).ShouldBeFalse();
        trade.IsOverdue(Now.AddHours(1)).ShouldBeTrue();
    }

    [Test]
    public void OpensOnceAndClosesByTheTarget()
    {
        ScanTrade trade = Trade();

        trade.Open(Now.AddMinutes(5)).ShouldBeTrue();
        trade.Open(Now.AddMinutes(6)).ShouldBeFalse();
        trade.IsOverdue(Now.AddHours(2)).ShouldBeFalse();
        trade.RecordExit(ScanTradeExit.Target).ShouldBeTrue();
        trade.Close(realizedPnl: 4.2m, Now.AddMinutes(30)).ShouldBeTrue();

        (trade.Status, trade.OpenedAt, trade.ClosedAt, trade.RealizedPnl).ShouldBe((ScanTradeStatus.TookProfit, Now.AddMinutes(5), Now.AddMinutes(30), 4.2m));
        trade.Close(realizedPnl: 1m, Now.AddMinutes(31)).ShouldBeFalse();
    }

    [Test]
    public void AnExitAfterTheCloseNamesTheStatus()
    {
        ScanTrade trade = Trade();
        trade.Open(Now);

        trade.Close(realizedPnl: -2m, Now.AddMinutes(10)).ShouldBeTrue();
        trade.Status.ShouldBe(ScanTradeStatus.Closed);
        trade.RecordExit(ScanTradeExit.Stop).ShouldBeTrue();
        trade.Status.ShouldBe(ScanTradeStatus.StoppedOut);
        trade.RecordExit(ScanTradeExit.Target).ShouldBeFalse();
    }

    [Test]
    public void ACloseWithoutTheStrategysSaleIsClosed()
    {
        ScanTrade trade = Trade();
        trade.Open(Now);

        trade.Close(realizedPnl: 0.5m, Now.AddMinutes(10));

        trade.Status.ShouldBe(ScanTradeStatus.Closed);
    }

    [TestCase(ScanTradeStatus.Expired)]
    [TestCase(ScanTradeStatus.Cancelled)]
    [TestCase(ScanTradeStatus.Failed)]
    public void OnlyAWaitingTradeEndsWithoutAPosition(ScanTradeStatus status)
    {
        ScanTrade waiting = Trade();
        ScanTrade open = Trade();
        open.Open(Now);

        End(waiting, status).ShouldBeTrue();
        End(open, status).ShouldBeFalse();

        (waiting.Status, waiting.ClosedAt, waiting.IsLive).ShouldBe((status, Now.AddHours(1), false));
        waiting.FailureCode.ShouldBe(status == ScanTradeStatus.Failed ? "scan_trade_entry_ended" : null);
        open.Status.ShouldBe(ScanTradeStatus.Open);
    }

    [Test]
    public void OnlyAFinishedTradeReleasesItsBotOnce()
    {
        ScanTrade trade = Trade();

        trade.ReleaseBot().ShouldBeFalse();
        trade.Expire(Now.AddHours(1));
        trade.ReleaseBot().ShouldBeTrue();
        trade.ReleaseBot().ShouldBeFalse();
        trade.BotReleased.ShouldBeTrue();
    }

    [Test]
    public void TakesAProposalWithItsScenarioAndPrices()
    {
        ScanTrade trade = Trade();

        trade.Takes(ScanScenario.Levels, entry: 100m, stop: 95m, target: 110m).ShouldBeTrue();
        trade.Takes(ScanScenario.Target2R, entry: 100m, stop: 95m, target: 110m).ShouldBeFalse();
        trade.Takes(ScanScenario.Levels, entry: 100m, stop: 95m, target: 112m).ShouldBeFalse();
    }

    [TestCase(arg1: 100, arg2: 100, arg3: 110)]
    [TestCase(arg1: 100, arg2: 95, arg3: 100)]
    [TestCase(arg1: 100, arg2: 0, arg3: 110)]
    public void RefusesLevelsOutOfOrder(decimal entry, decimal stop, decimal target)
    {
        Should.Throw<ScanTradeRuleException>(() => Trade(entry, stop, target)).Code.ShouldBe(ScanTradeRuleException.InvalidLevels);
    }

    private static bool End(ScanTrade trade, ScanTradeStatus status)
    {
        DateTimeOffset at = Now.AddHours(1);

        return status switch
        {
            ScanTradeStatus.Expired => trade.Expire(at),
            ScanTradeStatus.Cancelled => trade.Cancel(at),
            _ => trade.Fail("scan_trade_entry_ended", at)
        };
    }

    private static ScanTrade Trade(decimal entry = 100m, decimal stop = 95m, decimal target = 110m)
    {
        return ScanTrade.Create(
            ScanTradeId.New(),
            UserId.New(),
            new VenueId("bybit"),
            "HYPEUSDT",
            ScanScenario.Levels,
            Timeframe.Parse("1h"),
            entry,
            stop,
            target,
            amount: 100m,
            risk: 5.2m,
            BotId.New(),
            TimeSpan.FromHours(1),
            Now);
    }
}