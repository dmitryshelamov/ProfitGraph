using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Domain.Scanner.AutoTrades;

/// <summary>
///     An auto trade rule (ADR 0071, sections 4, 5 and 9): created off, it trades on paper only with a proof of two backtests of
///     opposite markets in which its cards earned enough in each third; a changed card, changed limits or changed preset conditions
///     switch it off; it pauses for a UTC day; it tracks entering pairs only while on, and the first check after a switch records only.
/// </summary>
public sealed class ScanAutoTradeRuleTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 31, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanAutoTradeThresholds Thresholds = new ScanAutoTradeThresholds(MinBacktestTrades: 100, MinBacktestMeanR: 0.05m, MinPaperTrades: 30);

    private static readonly ScanAutoTradeCard Card =
        new ScanAutoTradeCard(PositionDirection.Long, ScanScenario.Target2R, "1h", minExpectedR: 0.1m, minChance: null);

    private static readonly ScanAutoTradeLimits Limits = new ScanAutoTradeLimits(maxOpenTrades: 3, maxTradesPerDay: 10, dailyLossLimit: 15m);

    [Test]
    public void ARuleIsCreatedOff()
    {
        ScanAutoTradeRule rule = Rule();

        (rule.Mode, rule.Proof, rule.IsOn, rule.ModeSince).ShouldBe((ScanAutoTradeMode.Off, null, false, Now));
        rule.Check(["SOLUSDT"], Now).ShouldBeEmpty();
        rule.CheckedAt.ShouldBeNull();
    }

    [Test]
    public void AProofOfTwoOppositeMarketsSwitchesItToPaperAndTheFirstCheckOnlyRecords()
    {
        ScanAutoTradeRule rule = Rule();
        ScanAutoTradeProof proof = Proof();

        rule.SwitchToPaper(proof, Thresholds, Now.AddMinutes(1));

        (rule.Mode, rule.Proof, rule.ModeSince).ShouldBe((ScanAutoTradeMode.Paper, proof, Now.AddMinutes(1)));
        rule.Check(["SOLUSDT"], Now.AddMinutes(2)).ShouldBeEmpty();
        rule.Check(["SOLUSDT", "HYPEUSDT"], Now.AddMinutes(3)).ShouldBe(["HYPEUSDT"]);
    }

    [Test]
    public void ProofsThatDoNotEarnInEachThirdOfBothMarketsAreRefused()
    {
        // A weak third, too few trades, the same market twice and the same backtest twice each fail.
        ScanAutoTradeEvidence weak = Evidence(change: 5m, meanR: [0.2m, 0.01m, 0.2m]);
        ScanAutoTradeEvidence few = Evidence(change: 5m, trades: 20);
        ScanAutoTradeEvidence rising = Evidence(change: 5m);
        ScanAutoTradeEvidence falling = Evidence(change: -5m);

        foreach (ScanAutoTradeProof proof in new[]
                 {
                     new ScanAutoTradeProof(weak, falling), new ScanAutoTradeProof(few, falling), new ScanAutoTradeProof(rising, Evidence(change: 3m)),
                     new ScanAutoTradeProof(rising, rising)
                 })
        {
            ScanAutoTradeRule rule = Rule();

            Should.Throw<ScanAutoTradeRuleException>(() => rule.SwitchToPaper(proof, Thresholds, Now)).Code.ShouldBe(ScanAutoTradeRuleException.NotProven);
            rule.Mode.ShouldBe(ScanAutoTradeMode.Off);
        }
    }

    [Test]
    public void OnlyAnOffRuleStartsPaperTrading()
    {
        ScanAutoTradeRule rule = Paper();

        Should.Throw<ScanAutoTradeRuleException>(() => rule.SwitchToPaper(Proof(), Thresholds, Now)).Code.ShouldBe(ScanAutoTradeRuleException.WrongOrder);
    }

    [Test]
    public void SwitchingOffForgetsTheProofAndThePause()
    {
        ScanAutoTradeRule rule = Paper();
        rule.Pause(new DateOnly(year: 2026, month: 10, day: 31), Now);

        rule.SwitchOff(Now.AddHours(1));

        (rule.Mode, rule.Proof, rule.PausedOn, rule.ModeSince).ShouldBe((ScanAutoTradeMode.Off, null, null, Now.AddHours(1)));
    }

    [Test]
    public void AChangedCardOrLimitsSwitchTheRuleOffAndTheSameOnesDoNot()
    {
        ScanAutoTradeRule same = Paper();
        same.Change(Card, Limits, Now.AddMinutes(5));
        same.Mode.ShouldBe(ScanAutoTradeMode.Paper);

        ScanAutoTradeRule changed = Paper();
        changed.Change(Card, new ScanAutoTradeLimits(maxOpenTrades: 5, maxTradesPerDay: 10, dailyLossLimit: 15m), Now.AddMinutes(5));
        (changed.Mode, changed.Limits.MaxOpenTrades).ShouldBe((ScanAutoTradeMode.Off, 5));
    }

    [Test]
    public void ChangedPresetConditionsSwitchTheRuleOff()
    {
        ScanAutoTradeRule rule = Paper();

        rule.ConditionsChanged(Now.AddMinutes(5));

        (rule.Mode, rule.Proof).ShouldBe((ScanAutoTradeMode.Off, null));
    }

    [Test]
    public void APausedRuleTradesAgainTheNextDay()
    {
        ScanAutoTradeRule rule = Paper();
        var today = new DateOnly(year: 2026, month: 10, day: 31);

        rule.Pause(today, Now);

        (rule.PausedOn, rule.TradesOn(today), rule.TradesOn(today.AddDays(1))).ShouldBe((today, false, true));

        // An off rule does not pause.
        ScanAutoTradeRule off = Rule();
        off.Pause(today, Now);
        off.PausedOn.ShouldBeNull();
    }

    [Test]
    public void LimitsAdmitATradeUntilOneOfThemIsReached()
    {
        Limits.Admits(openTrades: 2, todayTrades: 9, todayLoss: 14.99m).ShouldBeNull();
        Limits.Admits(openTrades: 3, todayTrades: 0, todayLoss: 0m).ShouldBe(ScanAutoTradeSkip.LimitOpen);
        Limits.Admits(openTrades: 0, todayTrades: 10, todayLoss: 0m).ShouldBe(ScanAutoTradeSkip.LimitDay);
        Limits.Admits(openTrades: 0, todayTrades: 0, todayLoss: 15m).ShouldBe(ScanAutoTradeSkip.Paused);
        Should.Throw<ScanAutoTradeRuleException>(() => new ScanAutoTradeLimits(maxOpenTrades: 0, maxTradesPerDay: 10, dailyLossLimit: 15m));
    }

    [Test]
    public void ACardPassesItsMinimums()
    {
        var picky = new ScanAutoTradeCard(PositionDirection.Long, ScanScenario.Levels, "15m", minExpectedR: 0.1m, minChance: 0.4m);

        (picky.Accepts(expectedR: 0.1m, chance: 0.4m), picky.Accepts(expectedR: 0.09m, chance: 0.5m), picky.Accepts(expectedR: 0.2m, chance: 0.39m))
            .ShouldBe((true, false, false));
        Card.Accepts(expectedR: 0.1m, chance: 0.01m).ShouldBeTrue();
    }

    private static ScanAutoTradeRule Rule()
    {
        return ScanAutoTradeRule.Create(new UserId(Guid.NewGuid()), ScanPresetId.New(), new VenueId("bybit"), Card, Limits, Now);
    }

    private static ScanAutoTradeRule Paper()
    {
        ScanAutoTradeRule rule = Rule();
        rule.SwitchToPaper(Proof(), Thresholds, Now);

        return rule;
    }

    private static ScanAutoTradeProof Proof()
    {
        return new ScanAutoTradeProof(Evidence(change: 7m), Evidence(change: -7m));
    }

    private static ScanAutoTradeEvidence Evidence(decimal change, int trades = 60, decimal[]? meanR = null)
    {
        decimal[] means = meanR ?? [0.1m, 0.08m, 0.12m];

        return new ScanAutoTradeEvidence(ScanBacktestId.New(), [.. means.Select(mean => new ScanAutoTradeThird(trades, mean))], change);
    }
}