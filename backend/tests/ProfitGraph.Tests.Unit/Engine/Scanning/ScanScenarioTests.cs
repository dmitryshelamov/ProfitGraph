using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     The trade scenarios after a signal (ADR 0059, section 8): entry at the next open or on a pullback, exits at the stop, the target or
///     after 24 hours, the stop first when one minute touches both, a gap past a level at the open, and results in R after a 0.1 % fee on
///     each side.
/// </summary>
public sealed class ScanScenarioTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanMetrics AtLevels = new ScanMetrics { Support = 98m, Resistance = 104m };

    [Test]
    public void LevelsTradeReachesTheResistance()
    {
        // The signal is minute 0; minute 1 opens at 100, minute 3 trades up to 104.5.
        List<ScanCandle> minutes = Flat(count: 10, price: 100m);
        minutes[3] = minutes[3] with { High = 104.5m };

        List<ScanTrade> trades = ScanStudy.Trades(minutes, index: 0, AtLevels);

        ScanTrade levels = trades.Single(trade => trade.Scenario == ScanScenario.Levels);
        (levels.Entry, levels.Stop, levels.Target, levels.Exit, levels.Reason).ShouldBe((100m, 98m, 104m, 104m, ScanTradeExit.Target));
        levels.ResultR.ShouldBe((104m - 100m - 204m * 0.001m) / 2m);
        levels.ResultPercent.ShouldBe((104m - 100m - 204m * 0.001m) / 100m * 100m);
        (levels.EnteredAt, levels.ExitedAt).ShouldBe((Start.AddMinutes(1), Start.AddMinutes(4)));
        trades.Select(trade => trade.Scenario).ShouldBe([ScanScenario.Levels, ScanScenario.LevelsRewardRisk2, ScanScenario.Target2R]);
    }

    [Test]
    public void WithTheHoursAverageRangeTheStopLiesBelowTheSupportAndANearOneTradesNothing()
    {
        // A range of 2 moves the stop from the support of 98 to 97.5, 2.5 points away; minute 3 dips to 97.4.
        List<ScanCandle> minutes = Flat(count: 10, price: 100m);
        minutes[3] = minutes[3] with { Low = 97.4m };

        ScanTrade levels = ScanStudy.Trades(minutes, index: 0, AtLevels, averageRange: 2m).Single(trade => trade.Scenario == ScanScenario.Levels);

        (levels.Stop, levels.Exit, levels.Reason).ShouldBe((97.5m, 97.5m, ScanTradeExit.Stop));

        // A range of 4 puts the stop at 97, 3 points away, nearer than one range: no level trade.
        ScanStudy.Trades(minutes, index: 0, AtLevels, averageRange: 4m).ShouldBeEmpty();
    }

    [Test]
    public void AMinuteTouchingStopAndTargetCountsAsTheStop()
    {
        List<ScanCandle> minutes = Flat(count: 10, price: 100m);
        minutes[2] = minutes[2] with { High = 110m, Low = 97m };

        ScanTrade levels = ScanStudy.Trades(minutes, index: 0, AtLevels).Single(trade => trade.Scenario == ScanScenario.Levels);

        (levels.Exit, levels.Reason).ShouldBe((98m, ScanTradeExit.Stop));
        levels.ResultR.ShouldBe((98m - 100m - 198m * 0.001m) / 2m);
    }

    [Test]
    public void AGapBelowTheStopLeavesAtTheOpen()
    {
        List<ScanCandle> minutes = Flat(count: 10, price: 100m);
        minutes[2] = new ScanCandle(minutes[2].OpenTime, Open: 95m, High: 95m, Low: 94m, Close: 94m, Volume: 1, Turnover: 1);

        ScanStudy.Trades(minutes, index: 0, AtLevels).Single(trade => trade.Scenario == ScanScenario.Levels).Exit.ShouldBe(95m);
    }

    [Test]
    public void ATradeLeavesAfterADayAtThatMinutesClose()
    {
        List<ScanCandle> minutes = Flat(count: 1500, price: 100m);
        minutes[1440] = minutes[1440] with { Close = 101m, High = 101m };

        ScanTrade levels = ScanStudy.Trades(minutes, index: 0, AtLevels).Single(trade => trade.Scenario == ScanScenario.Levels);

        (levels.Exit, levels.Reason, levels.ExitedAt).ShouldBe((101m, ScanTradeExit.Time, Start.AddMinutes(1441)));
    }

    [Test]
    public void ATradeTheHistoryDoesNotCloseIsLeftOut()
    {
        ScanStudy.Trades(Flat(count: 100, price: 100m), index: 0, AtLevels).ShouldBeEmpty();
    }

    [Test]
    public void WithoutASupportOnlyThePullbackCanTrade()
    {
        List<ScanCandle> minutes = Flat(count: 10, price: 100m);
        minutes[3] = minutes[3] with { High = 110m };

        ScanStudy.Trades(minutes, index: 0, new ScanMetrics { Resistance = 104m }).ShouldBeEmpty();
    }

    [Test]
    public void APullbackFillsHalfwayDownTheLastHourAndAimsAtTwoRisks()
    {
        // The last hour rose from a low of 90 to a close of 100: the limit is 95, the stop 90 and the target 105.
        List<ScanCandle> minutes = Flat(count: 200, price: 100m);
        minutes[0] = minutes[0] with { Low = 90m };
        minutes[70] = new ScanCandle(minutes[70].OpenTime, Open: 96m, High: 96m, Low: 94.5m, Close: 95m, Volume: 1, Turnover: 1);
        minutes[90] = minutes[90] with { High = 105.5m };

        ScanTrade pullback = ScanStudy.Trades(minutes, index: 59, new ScanMetrics()).ShouldHaveSingleItem();

        (pullback.Scenario, pullback.Entry, pullback.Stop, pullback.Target).ShouldBe((ScanScenario.Pullback, 95m, 90m, 105m));
        (pullback.EnteredAt, pullback.Exit, pullback.Reason).ShouldBe((Start.AddMinutes(70), 105m, ScanTradeExit.Target));
    }

    [Test]
    public void AShortLevelsTradeFallsToTheSupport()
    {
        // Entry 100 at minute 1, stop at the resistance 104, target at the support 98; minute 3 trades down to 97.5.
        List<ScanCandle> minutes = Flat(count: 10, price: 100m);
        minutes[3] = minutes[3] with { Low = 97.5m };

        List<ScanTrade> trades = ScanStudy.Trades(minutes, index: 0, AtLevels, shorts: true);

        ScanTrade levels = trades.Single(trade => trade is { Scenario: ScanScenario.Levels, Direction: PositionDirection.Short });
        (levels.Entry, levels.Stop, levels.Target, levels.Exit, levels.Reason).ShouldBe((100m, 104m, 98m, 98m, ScanTradeExit.Target));
        levels.ResultR.ShouldBe((100m - 98m - 198m * 0.001m) / 4m);

        // Reward-to-risk 0.5 takes no «R:R ≥ 2» trade; the 2R target at 92 is not reached before the history ends.
        trades.Where(trade => trade.Direction == PositionDirection.Short).Select(trade => trade.Scenario).ShouldBe([ScanScenario.Levels]);
        ScanStudy.Trades(minutes, index: 0, AtLevels).ShouldAllBe(trade => trade.Direction == PositionDirection.Long);
    }

    [Test]
    public void AShortStopsAtAHighAndAGapAboveLeavesAtTheOpen()
    {
        List<ScanCandle> minutes = Flat(count: 10, price: 100m);
        minutes[2] = new ScanCandle(minutes[2].OpenTime, Open: 106m, High: 107m, Low: 105m, Close: 106m, Volume: 1, Turnover: 1);

        ScanTrade levels = ScanStudy.Trades(minutes, index: 0, AtLevels, shorts: true)
            .Single(trade => trade is { Scenario: ScanScenario.Levels, Direction: PositionDirection.Short });

        (levels.Exit, levels.Reason).ShouldBe((106m, ScanTradeExit.Stop));
        levels.ResultR.ShouldBe((100m - 106m - 206m * 0.001m) / 4m);
    }

    [Test]
    public void AShortPullbackFillsHalfwayUpTheLastHourFall()
    {
        // The last hour fell from a high of 110 to a close of 100: the limit is 105, the stop 110 and the target 95.
        List<ScanCandle> minutes = Flat(count: 200, price: 100m);
        minutes[0] = minutes[0] with { High = 110m };
        minutes[70] = new ScanCandle(minutes[70].OpenTime, Open: 104m, High: 105.5m, Low: 104m, Close: 105m, Volume: 1, Turnover: 1);
        minutes[90] = minutes[90] with { Low = 94.5m };

        ScanTrade rally = ScanStudy.Trades(minutes, index: 59, new ScanMetrics(), shorts: true)
            .Single(trade => trade.Direction == PositionDirection.Short);

        (rally.Scenario, rally.Entry, rally.Stop, rally.Target).ShouldBe((ScanScenario.Pullback, 105m, 110m, 95m));
        (rally.EnteredAt, rally.Exit, rally.Reason).ShouldBe((Start.AddMinutes(70), 95m, ScanTradeExit.Target));
    }

    [Test]
    public void TheSummaryListsShortScenariosOnlyWhereShortsAreAllowed()
    {
        ScanSignal[] signals = [Signal(Start, resultR: 1m)];

        ScanStudy.Summarize(signals).Scenarios!.ShouldAllBe(item => item.Direction == PositionDirection.Long);
        ScanStudy.Summarize(signals, shorts: true).Scenarios!.Count(item => item.Direction == PositionDirection.Short).ShouldBe(4);
    }

    [Test]
    public void TheSummaryCountsWinsTotalsAndTheLongestLosingRun()
    {
        ScanSignal[] signals =
        [
            Signal(Start, resultR: 2m), Signal(Start.AddHours(1), resultR: -1m), Signal(Start.AddHours(2), resultR: -1m), Signal(Start.AddHours(3), resultR: 1m)
        ];

        ScanScenarioSummary summary = ScanStudy.Summarize(signals).Scenarios!.Single(item => item.Scenario == ScanScenario.Target2R);

        (summary.Trades, summary.WinningShare, summary.TotalR, summary.MeanR, summary.MedianR, summary.LongestLosingStreak)
            .ShouldBe((4, (decimal?)0.5m, 1m, (decimal?)0.25m, (decimal?)0m, 2));
        summary.MeanMinutes.ShouldBe(10m);
    }

    private static ScanSignal Signal(DateTimeOffset at, decimal resultR)
    {
        var trade = new ScanTrade(
            ScanScenario.Target2R,
            Entry: 100,
            Stop: 98,
            Target: 104,
            at,
            at.AddMinutes(10),
            Exit: 100,
            ScanTradeExit.Time,
            resultR,
            ResultPercent: 0);

        return new ScanSignal(
            "X",
            at,
            EntryPrice: 100,
            new ScanMetrics(),
            Change15m: null,
            Change1h: null,
            Change4h: null,
            Change24h: null,
            MaxRise24h: null,
            MaxDrop24h: null,
            [trade]);
    }

    private static List<ScanCandle> Flat(int count, decimal price)
    {
        return [.. Enumerable.Range(start: 0, count).Select(index => new ScanCandle(Start.AddMinutes(index), price, price, price, price, Volume: 1, Turnover: 1))];
    }
}