using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     The trade cards' proposals (ADR 0060) follow the preset backtest's scenarios from the current price.
/// </summary>
public sealed class ScanTradeIdeasTests
{
    [Test]
    public void ProposesEveryScenarioBetweenTheLevels()
    {
        IReadOnlyList<ScanTradeIdea> ideas = ScanTradeIdeas.Of(price: 100m, support: 95m, resistance: 112m, lastHourLow: 90m);

        ideas.ShouldBe(
        [
            new ScanTradeIdea(ScanScenario.Levels, Entry: 100m, Stop: 95m, Target: 112m, 12m / 5m, ScanTradeIdeas.MarketEntryWait),
            new ScanTradeIdea(ScanScenario.Target2R, Entry: 100m, Stop: 95m, Target: 110m, RewardRisk: 2m, ScanTradeIdeas.MarketEntryWait),

            // Halfway down from 100 to the hour's low of 90.
            new ScanTradeIdea(ScanScenario.Pullback, Entry: 95m, Stop: 90m, Target: 105m, RewardRisk: 2m, ScanTradeIdeas.PullbackWait)
        ]);
    }

    [Test]
    public void LevelsNeedAResistanceAboveAndASupportBelow()
    {
        ScanTradeIdeas.Of(price: 100m, support: 95m, resistance: null, lastHourLow: null).Select(idea => idea.Scenario).ShouldBe([ScanScenario.Target2R]);
        ScanTradeIdeas.Of(price: 100m, support: 101m, resistance: 112m, lastHourLow: 100m).ShouldBeEmpty();
    }

    [Test]
    public void WithTheAverageRangeAStopLiesBeyondItsLevelAndANearStopIsLeftOut()
    {
        // A range of 2 puts the stop half a point below the support of 95; the pullback keeps the hour's low.
        IReadOnlyList<ScanTradeIdea> ideas = ScanTradeIdeas.Of(price: 100m, support: 95m, resistance: 112m, lastHourLow: 90m, averageRange: 2m);

        ideas.Take(2)
            .ShouldBe(
            [
                new ScanTradeIdea(ScanScenario.Levels, Entry: 100m, Stop: 94.5m, Target: 112m, 12m / 5.5m, ScanTradeIdeas.MarketEntryWait),
                new ScanTradeIdea(ScanScenario.Target2R, Entry: 100m, Stop: 94.5m, Target: 111m, RewardRisk: 2m, ScanTradeIdeas.MarketEntryWait)
            ]);
        ideas[2].Stop.ShouldBe(90m);

        // With a range of 8 the stop at 93 is 7 points away, nearer than one range, so only the pullback stays.
        ScanTradeIdeas.Of(price: 100m, support: 95m, resistance: 112m, lastHourLow: 90m, averageRange: 8m)
            .Select(idea => idea.Scenario)
            .ShouldBe([ScanScenario.Pullback]);
    }

    [Test]
    public void PricesFromLevelsKeepEightSignificantDigitsRoundedAgainstTheTrade()
    {
        // Levels are means with endless thirds: 99.99966… below and 113.33… above. The stop rounds down, away from the entry, and the target
        // down, towards it, so a stored trade (18 decimals) keeps the card's prices.
        ScanTradeIdea levels = ScanTradeIdeas.Of(price: 105m, support: 299.999m / 3m, resistance: 340m / 3m, lastHourLow: null)[0];

        (levels.Stop, levels.Target).ShouldBe((99.999666m, 113.33333m));
    }

    [Test]
    public void AShortStopLiesAboveItsLevelByTheAverageRange()
    {
        ScanTradeIdeas.Of(price: 100m, support: 88m, resistance: 105m, lastHourLow: null, lastHourHigh: null, shorts: true, averageRange: 2m)
            .Where(idea => idea.Direction == PositionDirection.Short)
            .Select(idea => (idea.Scenario, idea.Stop, idea.Target))
            .ShouldBe([(ScanScenario.Levels, 105.5m, 88m), (ScanScenario.Target2R, 105.5m, 89m)]);
    }

    [Test]
    public void SizesTheAmountSoTheStopLosesTheRisk()
    {
        ScanTradeIdeas.Amount(risk: 10m, entry: 100m, stop: 90m, ScanTradeCosts.Flat(feeRate: 0m)).ShouldBe(100m);
        Should.Throw<ArgumentException>(() => ScanTradeIdeas.Amount(risk: 10m, entry: 100m, stop: 100m, ScanTradeCosts.Flat(feeRate: 0m)));
    }

    [Test]
    public void ProposesTheMirrorShortsWhereShortsAreAllowed()
    {
        IReadOnlyList<ScanTradeIdea> shorts =
        [
            .. ScanTradeIdeas.Of(price: 100m, support: 88m, resistance: 105m, lastHourLow: null, lastHourHigh: 110m, shorts: true)
                .Where(idea => idea.Direction == PositionDirection.Short)
        ];

        shorts.ShouldBe(
        [
            new ScanTradeIdea(ScanScenario.Levels, Entry: 100m, Stop: 105m, Target: 88m, 12m / 5m, ScanTradeIdeas.MarketEntryWait, PositionDirection.Short),
            new ScanTradeIdea(ScanScenario.Target2R, Entry: 100m, Stop: 105m, Target: 90m, RewardRisk: 2m, ScanTradeIdeas.MarketEntryWait, PositionDirection.Short),

            // Halfway up from 100 to the hour's high of 110.
            new ScanTradeIdea(ScanScenario.Pullback, Entry: 105m, Stop: 110m, Target: 95m, RewardRisk: 2m, ScanTradeIdeas.PullbackWait, PositionDirection.Short)
        ]);
        ScanTradeIdeas.Of(price: 100m, support: 88m, resistance: 105m, lastHourLow: null, lastHourHigh: 110m)
            .ShouldAllBe(idea => idea.Direction == PositionDirection.Long);
    }

    [Test]
    public void AShortTargetStaysAboveZero()
    {
        // Two risks below 10 from a stop at 20 would be −10.
        ScanTradeIdeas.Of(price: 10m, support: null, resistance: 20m, lastHourLow: null, lastHourHigh: null, shorts: true).ShouldBeEmpty();
    }

    [Test]
    public void SizesAShortSoItsStopAboveLosesTheRisk()
    {
        ScanTradeIdeas.Amount(risk: 10m, entry: 100m, stop: 110m, ScanTradeCosts.Flat(feeRate: 0m)).ShouldBe(100m);
    }
}