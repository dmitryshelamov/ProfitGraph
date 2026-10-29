using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Tests.Unit.Domain.Scanner;

/// <summary>
///     A preset's alert watch (ADR 0059, section 5): the first check only records the result, a pair is reported when it enters the
///     result, at most once an hour, an older snapshot changes nothing, and a restart records the result again.
/// </summary>
public sealed class ScanAlertWatchTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 12, minute: 0, second: 5, TimeSpan.Zero);

    [Test]
    public void TheFirstCheckOnlyRecordsTheResult()
    {
        ScanAlertWatch watch = Watch();

        watch.Check(["HYPEUSDT", "SOLUSDT"], Now).ShouldBeEmpty();

        watch.CheckedAt.ShouldBe(Now);
        watch.Pairs.Keys.ShouldBe(["HYPEUSDT", "SOLUSDT"], ignoreOrder: true);
    }

    [Test]
    public void APairIsReportedWhenItEntersAndNotWhileItStays()
    {
        ScanAlertWatch watch = Watch();
        watch.Check(["SOLUSDT"], Now);

        watch.Check(["SOLUSDT", "HYPEUSDT", "BTCUSDT"], Now.AddMinutes(1)).ShouldBe(["BTCUSDT", "HYPEUSDT"]);
        watch.Check(["SOLUSDT", "HYPEUSDT", "BTCUSDT"], Now.AddMinutes(2)).ShouldBeEmpty();

        watch.Pairs["HYPEUSDT"].ShouldBe(new ScanAlertPair(InResult: true, Now.AddMinutes(1)));
    }

    [Test]
    public void APairThatLeavesAndComesBackWithinTheCooldownIsNotReportedAgain()
    {
        ScanAlertWatch watch = Watch();
        watch.Check([], Now);
        watch.Check(["HYPEUSDT"], Now.AddMinutes(1));

        watch.Check([], Now.AddMinutes(2)).ShouldBeEmpty();
        watch.Check(["HYPEUSDT"], Now.AddMinutes(30)).ShouldBeEmpty();
        watch.Check([], Now.AddMinutes(31));

        watch.Check(["HYPEUSDT"], Now.AddMinutes(61)).ShouldBe(["HYPEUSDT"]);
    }

    [Test]
    public void APairOutsideTheResultIsForgottenOnceItsCooldownEnds()
    {
        ScanAlertWatch watch = Watch();
        watch.Check(["SOLUSDT"], Now);
        watch.Check(["SOLUSDT", "HYPEUSDT"], Now.AddMinutes(1));

        watch.Check([], Now.AddMinutes(2));

        watch.Pairs.Keys.ShouldBe(["HYPEUSDT"]);
        watch.Pairs["HYPEUSDT"].InResult.ShouldBeFalse();

        watch.Check([], Now.AddMinutes(61));

        watch.Pairs.ShouldBeEmpty();
    }

    [Test]
    public void ASnapshotNotNewerThanTheLastCheckChangesNothing()
    {
        ScanAlertWatch watch = Watch();
        watch.Check([], Now);

        watch.Check(["HYPEUSDT"], Now).ShouldBeEmpty();
        watch.Check(["HYPEUSDT"], Now.AddMinutes(-1)).ShouldBeEmpty();

        watch.Pairs.ShouldBeEmpty();
        watch.CheckedAt.ShouldBe(Now);
    }

    [Test]
    public void ARestartRecordsTheResultAgainAndKeepsTheCooldowns()
    {
        ScanAlertWatch watch = Watch();
        watch.Check([], Now);
        watch.Check(["HYPEUSDT"], Now.AddMinutes(1));

        watch.Restart();

        watch.CheckedAt.ShouldBeNull();
        watch.Check(["BTCUSDT"], Now.AddMinutes(2)).ShouldBeEmpty();
        watch.Check(["BTCUSDT", "HYPEUSDT"], Now.AddMinutes(3)).ShouldBeEmpty();
        watch.Check(["BTCUSDT", "HYPEUSDT", "SOLUSDT"], Now.AddMinutes(4)).ShouldBe(["SOLUSDT"]);
    }

    private static ScanAlertWatch Watch()
    {
        return ScanAlertWatch.Start(ScanPresetId.New(), UserId.New(), Now.AddMinutes(-5));
    }
}