using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     The cards of the pair's page followed by the bot's rules in the preset backtest, their bands and odds (ADR 0071, sections 1 and
///     2): a limit entry and target fill when a minute trades through them, the stop leaves at a minute's close beyond it, results are in
///     R of the card's sizing after costs, and a card's chance and expected result come from cards like it.
/// </summary>
public sealed class ScanCardTests
{
    // What the stop exactly at 98 loses per quote: the 2 % move and the costs of a stopped trade.
    private const decimal Risk = 0.02m + 0.0035m;
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanTradeCosts Costs = new ScanTradeCosts(limitFeeRate: 0.001m, marketFeeRate: 0.002m, spread: 0.0005m);

    private static readonly ScanTradeIdea Long =
        new ScanTradeIdea(ScanScenario.Target2R, Entry: 100m, Stop: 98m, Target: 104m, RewardRisk: 2m, ScanTradeIdeas.MarketEntryWait);

    [Test]
    public void ALimitEntryFillsWhenAMinuteTradesThroughItAndTheTargetPaysItsRewardAfterCosts()
    {
        List<ScanCandle> minutes = Flat(count: 10);
        minutes[1] = minutes[1] with { Low = 99.9m };
        minutes[3] = minutes[3] with { High = 104.1m };

        ScanCardOutcome outcome = ScanCardFollow.Follow(minutes, index: 0, Long, Costs)!;

        outcome.ShouldBe(new ScanCardOutcome(Filled: true, ScanTradeExit.Target, (0.04m - 0.002m) / Risk, Start.AddMinutes(1), Start.AddMinutes(4), ExitPrice: 104m));
    }

    [Test]
    public void TheStopLeavesAtAMinutesCloseBeyondItAndATouchDoesNot()
    {
        List<ScanCandle> minutes = Flat(count: 10);
        minutes[1] = minutes[1] with { Low = 99.9m };

        // Minute 2 dips to 97 and closes above the stop; minute 4 closes at 97.5, below it.
        minutes[2] = minutes[2] with { Low = 97m, Close = 98.5m };
        minutes[4] = minutes[4] with { Low = 97m, Close = 97.5m };

        ScanCardOutcome outcome = ScanCardFollow.Follow(minutes, index: 0, Long, Costs)!;

        (outcome.Exit, outcome.ResultR, outcome.ExitedAt).ShouldBe((ScanTradeExit.Stop, (-0.025m - 0.0035m) / Risk, Start.AddMinutes(5)));
        outcome.ResultR!.Value.ShouldBeLessThan(-1m);
    }

    [Test]
    public void TheTargetCountsInAMinuteThatAlsoClosesBeyondTheStopButNotInTheFillingMinute()
    {
        List<ScanCandle> minutes = Flat(count: 10);
        minutes[1] = minutes[1] with { Low = 99.9m, High = 104.5m };
        minutes[3] = minutes[3] with { High = 104.1m, Low = 97m, Close = 97m };

        ScanCardOutcome outcome = ScanCardFollow.Follow(minutes, index: 0, Long, Costs)!;

        (outcome.Exit, outcome.EnteredAt, outcome.ExitedAt).ShouldBe((ScanTradeExit.Target, Start.AddMinutes(1), Start.AddMinutes(4)));
    }

    [Test]
    public void TheFillingMinutesCloseBeyondTheStopStopsTheTrade()
    {
        List<ScanCandle> minutes = Flat(count: 10);
        minutes[1] = minutes[1] with { Low = 97m, Close = 97.8m };

        ScanCardFollow.Follow(minutes, index: 0, Long, Costs)!.Exit.ShouldBe(ScanTradeExit.Stop);
    }

    [Test]
    public void AnEntryNoMinuteTradesThroughWithinItsWaitDoesNotFill()
    {
        // A minute at the entry is a touch, not a fill; the wait of an hour needs 60 minutes after the signal to be known.
        ScanCardFollow.Follow(Flat(count: 61), index: 0, Long, Costs).ShouldBe(ScanCardOutcome.Unfilled);
        ScanCardFollow.Follow(Flat(count: 30), index: 0, Long, Costs).ShouldBeNull();
    }

    [Test]
    public void ATradeHeldToTheHorizonLeavesAtThatMinutesCloseByMarket()
    {
        List<ScanCandle> minutes = Flat(count: 4400);
        minutes[1] = minutes[1] with { Low = 99.9m };

        ScanCardOutcome outcome = ScanCardFollow.Follow(minutes, index: 0, Long, Costs)!;

        (outcome.Exit, outcome.ResultR, outcome.ExitedAt).ShouldBe((ScanTradeExit.Time, -0.0035m / Risk, Start.AddMinutes(1) + ScanCardFollow.Horizon));

        // The history ends before the horizon: the outcome is not known yet.
        ScanCardFollow.Follow(minutes.Take(4000).ToList(), index: 0, Long, Costs).ShouldBeNull();
    }

    [Test]
    public void AShortSellsThroughItsEntryAndBuysBackThroughItsTarget()
    {
        var idea = new ScanTradeIdea(ScanScenario.Levels, Entry: 100m, Stop: 102m, Target: 96m, RewardRisk: 2m, ScanTradeIdeas.MarketEntryWait, PositionDirection.Short);
        List<ScanCandle> minutes = Flat(count: 10);
        minutes[1] = minutes[1] with { High = 100.1m };
        minutes[2] = minutes[2] with { Low = 95.9m };

        ScanCardOutcome outcome = ScanCardFollow.Follow(minutes, index: 0, idea, Costs)!;

        (outcome.Exit, outcome.ResultR).ShouldBe((ScanTradeExit.Target, (0.04m - 0.002m) / Risk));
    }

    [Test]
    public void BandsGroupCardsByTheRandomWalksChanceAndTheStopInHourlyRanges()
    {
        // 1 / (1 + RR): 0.2 for 4R sits on the first edge and stays in the lowest band; 1/3 for 2R; 0.5 for 1R; 2/3 for 0.5R.
        (ScanCardBands.Chance(4m), ScanCardBands.Chance(2m), ScanCardBands.Chance(1m), ScanCardBands.Chance(0.5m)).ShouldBe((0, 2, 3, 4));

        // A 2 % stop is one NATR of 2 % (the edge of the third band) and two NATRs of 1 %.
        (ScanCardBands.Stop(Long, natrPercent: 2m), ScanCardBands.Stop(Long, natrPercent: 1m), ScanCardBands.Stop(Long, natrPercent: 0.1m)).ShouldBe((2, 4, 5));
        ScanCardBands.Stop(Long, natrPercent: null).ShouldBeNull();

        // The random walk's chance itself, the stop in NATRs and the market band (ADR 0073).
        (ScanCardBands.Geometry(4m), ScanCardBands.Geometry(1m), ScanCardBands.Geometry(-1m)).ShouldBe((0.2m, 0.5m, 1m));
        (ScanCardBands.StopNatr(Long, natrPercent: 4m), ScanCardBands.StopNatr(Long, natrPercent: 0m)).ShouldBe((0.5m, null));
        (ScanCardBands.Market(0.3m), ScanCardBands.Market(0m), ScanCardBands.Market(-2m), ScanCardBands.Market(null)).ShouldBe((1, 0, 0, null));
    }

    [Test]
    public void FramesAggregateWholeCandlesOfTheTimeframeFromTheMinutes()
    {
        // Minutes from 00:05 to 00:44: the candle of 00:00 starts before them, those of 00:15 and 00:30 are whole.
        List<ScanCandle> minutes =
        [
            .. Enumerable.Range(start: 5, count: 40)
                .Select(minute => new ScanCandle(Start.AddMinutes(minute), minute, minute + 0.5m, minute - 0.5m, minute + 0.25m, Volume: 1, Turnover: 2))
        ];

        var frames = new ScanCandleFrames(minutes, Timeframe.Parse("15m"));

        frames.ClosedBy(Start.AddMinutes(45), count: 10)
            .ShouldBe(
            [
                new ScanCandle(Start.AddMinutes(15), Open: 15m, High: 29.5m, Low: 14.5m, Close: 29.25m, Volume: 15, Turnover: 30),
                new ScanCandle(Start.AddMinutes(30), Open: 30m, High: 44.5m, Low: 29.5m, Close: 44.25m, Volume: 15, Turnover: 30)
            ]);
        frames.ClosedBy(Start.AddMinutes(44), count: 10).Select(candle => candle.OpenTime).ShouldBe([Start.AddMinutes(15)]);
        frames.ClosedBy(Start.AddMinutes(45), count: 1).Select(candle => candle.OpenTime).ShouldBe([Start.AddMinutes(30)]);
    }

    [Test]
    public void ComposedCardsListAProposalOfTwoTimeframesOnceUnderTheFirst()
    {
        ScanTimeframeLevels[] levels =
        [
            new ScanTimeframeLevels("15m", Price: 100m, Support: 95m, Resistance: 112m, AverageRange: null),
            new ScanTimeframeLevels("1h", Price: 100m, Support: 95m, Resistance: 120m, AverageRange: null)
        ];

        IReadOnlyList<(string Timeframe, ScanTradeIdea Idea)> ideas = ScanTradeIdeas.Compose(
            price: 100m,
            levels,
            (90m, 101m),
            lastFourHours: null,
            hourRange: null,
            shorts: false);

        ideas.Select(item => (item.Timeframe, item.Idea.Scenario))
            .ShouldBe(
            [
                ("15m", ScanScenario.Levels), ("15m", ScanScenario.Target2R), ("1h", ScanScenario.Levels),
                (ScanTradeIdeas.PullbackTimeframe, ScanScenario.Pullback)
            ]);
    }

    [Test]
    public void TheStudyFollowsTheCardsOfEachSignalAndSumsThemUpWhateverTheirOrder()
    {
        // A price swinging between 95 and 105 every four hours gives levels on 15 minutes and hours; the minutes run past the horizon
        // of the trades of both signals.
        List<ScanCandle> minutes = Wave(count: 9000);
        var replay = new ScanReplay(minutes, listedAt: null);
        var setup = new ScanCardSetup([Timeframe.Parse("15m"), Timeframe.Parse("1h")], Pullback: true, Extremes: true, Shorts: true);
        ScanSignal[] signals = [Signal(minutes, index: 3000), Signal(minutes, index: 4500)];

        IReadOnlyList<ScanCardTrade> cards = ScanStudy.Cards("X", replay, signals, setup, Costs);

        cards.Select(card => (card.Timeframe, card.Scenario)).ShouldContain(("15m", ScanScenario.Levels));
        cards.Select(card => (card.Timeframe, card.Scenario)).ShouldContain((ScanTradeIdeas.PullbackTimeframe, ScanScenario.Pullback));
        cards.Select(card => card.Direction).Distinct().Count().ShouldBe(2);
        cards.ShouldAllBe(card => card.StopBand != null && (card.SignalAt == signals[0].At || card.SignalAt == signals[1].At));
        cards.Where(card => card.Outcome.Filled).ShouldAllBe(card => card.Outcome.ResultR != null);

        IReadOnlyList<ScanCardOdds> odds = ScanStudy.Odds(cards);

        odds.Sum(item => item.Cards).ShouldBe(cards.Count);
        odds.Sum(item => item.Filled).ShouldBe(cards.Count(card => card.Outcome.Filled));
        ScanStudy.Odds(cards.Reverse()).ShouldBe(odds);

        // Each card carries what its odds need, and a trade that left at its stop how far beyond it, in NATRs (ADR 0073).
        cards.ShouldAllBe(card => card.Geometry > 0m && card.Geometry <= 1m && card.StopNatr > 0m && card.CostNatr > 0m);
        cards.Where(card => card.Outcome.Exit == ScanTradeExit.Stop).ShouldAllBe(card => card.Slip >= 0m && card.Outcome.ExitPrice != null);
        cards.Where(card => card.Outcome.Exit != ScanTradeExit.Stop).ShouldAllBe(card => card.Slip == null);
        odds.ShouldAllBe(item => item.Geometry > 0m && item.EffectiveTrades <= item.Filled);

        // Costs above any stop's distance leave no card.
        ScanStudy.Cards("X", replay, signals, setup, new ScanTradeCosts(limitFeeRate: 0.05m, marketFeeRate: 0.05m, spread: 0.1m)).ShouldBeEmpty();
    }

    private static ScanSignal Signal(IReadOnlyList<ScanCandle> minutes, int index)
    {
        return new ScanSignal(
            "X",
            minutes[index].OpenTime.AddMinutes(1),
            minutes[index + 1].Open,
            new ScanMetrics { Natr24h = 1m },
            Change15m: null,
            Change1h: null,
            Change4h: null,
            Change24h: null,
            MaxRise24h: null,
            MaxDrop24h: null,
            Trades: null);
    }

    private static List<ScanCandle> Flat(int count)
    {
        return
        [
            .. Enumerable.Range(start: 0, count)
                .Select(index => new ScanCandle(Start.AddMinutes(index), Open: 100m, High: 100m, Low: 100m, Close: 100m, Volume: 1, Turnover: 1))
        ];
    }

    private static List<ScanCandle> Wave(int count)
    {
        return
        [
            .. Enumerable.Range(start: 0, count)
                .Select(index =>
                {
                    decimal open = Price(index);
                    decimal close = Price(index + 1);

                    return new ScanCandle(Start.AddMinutes(index), open, Math.Max(open, close) + 0.05m, Math.Min(open, close) - 0.05m, close, Volume: 1, Turnover: 100);
                })
        ];
    }

    private static decimal Price(int minute)
    {
        return Math.Round(100m + 5m * (decimal)Math.Sin(2 * Math.PI * minute / 240), decimals: 4);
    }
}