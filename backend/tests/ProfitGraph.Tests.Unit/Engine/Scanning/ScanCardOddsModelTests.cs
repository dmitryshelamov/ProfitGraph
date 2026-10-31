using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Engine.Scanning;

/// <summary>
///     A card's odds from the backtests' cards like it (ADR 0073): the chance starts from the random walk's <c>1 / (1 + RR)</c>, is shrunk
///     level by level towards what the level above predicts, moves with the market band, keeps the card's own geometry within a band and
///     has a cautious end; trades of one hour count as fewer independent ones; a stopped trade loses its band's slip over the card's stop
///     distance; a calibration scores the chances against outcomes.
/// </summary>
public sealed class ScanCardOddsModelTests
{
    private const decimal Prior = 50m;
    private const decimal Third = 0.333333m;
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly ScanTradeCosts Costs = new ScanTradeCosts(limitFeeRate: 0.001m, marketFeeRate: 0.002m, spread: 0.0005m);

    private static readonly ScanTradeIdea Long =
        new ScanTradeIdea(ScanScenario.Target2R, Entry: 100m, Stop: 98m, Target: 104m, RewardRisk: 2m, ScanTradeIdeas.MarketEntryWait);

    [Test]
    public void TheMeasuredShareIsShrunkTowardsTheRandomWalkAndTheCardKeepsItsOwnGeometry()
    {
        // 60 of 150 closed trades reached the target against 1/3 of a random walk: the kind shrinks to (60 + 50/3) / 200, the band to
        // (60 + 50 × that) / 200.
        ScanCardOddsModel model = Model(minimumTrades: 100, Odds(stopBand: 2, filled: 150, targets: 60, Third, missedMeanR: -1.2m));
        decimal win = ScanCardFollow.ResultR(Long, Long.Target, ScanTradeExit.Target, Costs);

        ScanCardChance chance = Of(model, Long);

        chance.Chance.ShouldBe(expected: 0.395834m, tolerance: 0.000002m);
        chance.ChanceLow.ShouldBe(expected: 0.351628m, tolerance: 0.00001m);
        (chance.FillShare, chance.MissedR, chance.Trades).ShouldBe((0.75m, -1.2m, 150));
        chance.ExpectedR.ShouldBe(Math.Round(0.75m * (chance.Chance * win + (1m - chance.Chance) * -1.2m), decimals: 6));
        chance.ExpectedRLow.ShouldBeLessThan(chance.ExpectedR);

        // A farther target of the same band has a smaller chance: the band's shift applies to its own 1 / (1 + RR).
        ScanTradeIdea farther = Long with { Target = 104.4m, RewardRisk = 2.2m };
        Of(model, farther).Chance.ShouldBe(expected: 0.373281m, tolerance: 0.000002m);
    }

    [Test]
    public void AKindWithTooFewTradesOrACardWithoutTheNatrGetsNoOdds()
    {
        ScanCardOdds[] odds = [Odds(stopBand: 2, filled: 150, targets: 60, Third)];

        Model(minimumTrades: 151, odds).Of("15m", Long, natrPercent: 2m, marketTrend: null, Costs).ShouldBeNull();
        Model(minimumTrades: 100, odds).Of("1h", Long, natrPercent: 2m, marketTrend: null, Costs).ShouldBeNull();
        Model(minimumTrades: 100, odds).Of("15m", Long, natrPercent: null, marketTrend: null, Costs).ShouldBeNull();
    }

    [Test]
    public void ABandWithFewTradesLeansOnItsKind()
    {
        // Four of four trades of the card's band reached the target, half of the kind's thousand others did.
        ScanCardOddsModel model = Model(
            minimumTrades: 100,
            Odds(stopBand: 2, filled: 4, targets: 4, Third),
            Odds(stopBand: 3, filled: 1000, targets: 500, Third));

        ScanCardChance chance = Of(model, Long);

        chance.Chance.ShouldBe(expected: 0.531473m, tolerance: 0.00001m);
        chance.Trades.ShouldBe(4);

        // A band with no trades of its own takes the kind's chance.
        ScanTradeIdea nearer = Long with { Stop = 99m, Target = 102m };
        Of(model, nearer).Chance.ShouldBe(expected: 0.493991m, tolerance: 0.00001m);
    }

    [Test]
    public void TheMarketBandMovesTheChanceWhenItsCardsEndedDifferently()
    {
        ScanCardOddsModel model = Model(
            minimumTrades: 100,
            Odds(stopBand: 2, filled: 100, targets: 60, Third, marketBand: 1),
            Odds(stopBand: 2, filled: 100, targets: 20, Third, marketBand: 0));

        decimal rising = Of(model, Long, marketTrend: 0.5m).Chance;
        decimal falling = Of(model, Long, marketTrend: -0.5m).Chance;
        decimal unknown = Of(model, Long, marketTrend: null).Chance;

        rising.ShouldBeGreaterThan(unknown);
        unknown.ShouldBeGreaterThan(falling);
        unknown.ShouldBe(expected: 0.4m, tolerance: 0.02m);
    }

    [Test]
    public void OddsStoredBeforeTheModelTakeTheirChanceBandsMiddle()
    {
        // Without its own geometry the band of 1/3 counts as 0.35: (60 + 17.5) / 200, then (60 + 50 × 0.3875) / 200, on the card's 1/3.
        ScanCardOddsModel model = Model(minimumTrades: 100, Odds(stopBand: 2, filled: 150, targets: 60, geometry: null, missedMeanR: -1.2m));

        ScanCardChance chance = Of(model, Long);

        chance.Chance.ShouldBe(expected: 0.379279m, tolerance: 0.000002m);
        chance.MissedR.ShouldBe(-1.2m);
    }

    [Test]
    public void AStoppedTradeLosesItsBandsSlipOverTheCardsStopDistanceAndCosts()
    {
        // The stopped trades left half an NATR beyond their stops. With an NATR of 2 % the 2 % stop is one NATR and the costs of a stopped
        // trade (0.35 %) 0.175 NATR; a 1.2 % stop of the same band is 0.6 NATR and loses more.
        ScanCardOddsModel model = Model(minimumTrades: 50, Odds(stopBand: 2, filled: 100, targets: 0, Third, stopSlip: 0.5m));
        ScanTradeIdea nearer = Long with { Stop = 98.8m, Target = 102.4m };

        Of(model, Long).MissedR.ShouldBe(Math.Round(-1m - 0.5m / 1.175m, decimals: 6));
        Of(model, nearer).MissedR.ShouldBe(Math.Round(-1m - 0.5m / 0.775m, decimals: 6));
    }

    [Test]
    public void TradesOfOneHourThatMovedTogetherCountAsFewerIndependentOnes()
    {
        // Ten hours of ten trades each, every trade of an hour ending alike, are worth ten trades; a hundred separate hours, a hundred.
        ScanCardTrade[] clustered = [.. Enumerable.Range(start: 0, count: 100).Select(index => Card(index, index / 10, index / 10 % 2 == 0))];
        ScanCardTrade[] separate = [.. Enumerable.Range(start: 0, count: 100).Select(index => Card(index, index, index % 2 == 0))];

        ScanCardOdds odds = ScanStudy.Odds(clustered).ShouldHaveSingleItem();

        (odds.Filled, odds.Targets, odds.EffectiveTrades, odds.Geometry).ShouldBe((100, 50, 10m, Third));
        ScanStudy.Odds(separate).ShouldHaveSingleItem().EffectiveTrades.ShouldBe(100m);

        // Fewer independent trades shrink the band harder towards the random walk.
        decimal worthTen = Of(Model(minimumTrades: 100, odds), Long).Chance;
        decimal worthHundred = Of(Model(minimumTrades: 100, [.. ScanStudy.Odds(separate)]), Long).Chance;
        worthTen.ShouldBeLessThan(worthHundred);
    }

    [Test]
    public void WeightsScaleEachBacktestsCounts()
    {
        // 60 of 150 at full weight and 15 of 150 at half weight pool to 67.5 of 225, as 135 of 450 at half weight do.
        var pooled = new ScanCardOddsModel(
            [
                new ScanWeightedOdds([Odds(stopBand: 2, filled: 150, targets: 60, Third)], Weight: 1m),
                new ScanWeightedOdds([Odds(stopBand: 2, filled: 150, targets: 15, Third)], Weight: 0.5m)
            ],
            Prior,
            minimumTrades: 100);
        var single = new ScanCardOddsModel([new ScanWeightedOdds([Odds(stopBand: 2, filled: 450, targets: 135, Third)], Weight: 0.5m)], Prior, minimumTrades: 100);

        Of(pooled, Long).Chance.ShouldBe(Of(single, Long).Chance, tolerance: 0.000002m);
    }

    [Test]
    public void TheCalibrationScoresTheChancesAgainstTheOutcomes()
    {
        ScanCardOddsModel model = Model(minimumTrades: 100, Odds(stopBand: 2, filled: 150, targets: 60, Third));
        ScanCardTrade[] cards =
        [
            Card(index: 0, hour: 0, target: true), Card(index: 1, hour: 1, target: false), Card(index: 2, hour: 2, target: false), Card(index: 3, hour: 3, target: true)
        ];
        var source = Guid.NewGuid();

        ScanCardCalibration calibration = ScanCardCalibration.Of([source], model, cards)!;
        decimal chance = model.Of(cards[0])!.Chance;

        calibration.Sources.ShouldBe([source]);
        (calibration.Cards, calibration.Observed, calibration.Predicted).ShouldBe((4, 0.5m, chance));
        calibration.Brier.ShouldBe(Math.Round(((1m - chance) * (1m - chance) + chance * chance) / 2m, decimals: 6));
        calibration.GeometryBrier.ShouldBe(Math.Round(((1m - Third) * (1m - Third) + Third * Third) / 2m, decimals: 6));
        calibration.BandsBrier.ShouldBe(Math.Round((0.6m * 0.6m + 0.4m * 0.4m) / 2m, decimals: 6));
        calibration.Bins.ShouldHaveSingleItem().ShouldBe(new ScanCalibrationBin(Lower: 0.3m, Upper: 0.4m, Cards: 4, chance, Observed: 0.5m));

        // Cards of a kind without odds are not scored.
        ScanCardCalibration.Of([source], model, [cards[0] with { Timeframe = "4h" }]).ShouldBeNull();
    }

    private static ScanCardOddsModel Model(int minimumTrades, params ScanCardOdds[] odds)
    {
        return new ScanCardOddsModel([new ScanWeightedOdds(odds, Weight: 1m)], Prior, minimumTrades);
    }

    private static ScanCardOdds Odds(
        int stopBand,
        int filled,
        int targets,
        decimal? geometry,
        int? marketBand = null,
        decimal? missedMeanR = null,
        decimal? stopSlip = null)
    {
        return new ScanCardOdds(
            "15m",
            ScanScenario.Target2R,
            PositionDirection.Long,
            ScanCardBands.Chance(2m),
            stopBand,
            filled * 4 / 3,
            filled,
            targets,
            filled - targets,
            MeanR: 0m,
            missedMeanR,
            marketBand,
            geometry,
            geometry is null ? null : filled,
            stopSlip,
            HeldMeanR: null);
    }

    private static ScanCardChance Of(ScanCardOddsModel model, ScanTradeIdea idea, decimal? marketTrend = null)
    {
        return model.Of("15m", idea, natrPercent: 2m, marketTrend, Costs)!;
    }

    private static ScanCardTrade Card(int index, int hour, bool target)
    {
        ScanTradeExit exit = target ? ScanTradeExit.Target : ScanTradeExit.Stop;
        var outcome = new ScanCardOutcome(Filled: true, exit, target ? 1.8m : -1m, Start.AddHours(hour), Start.AddHours(hour + 1));

        return new ScanCardTrade(
            $"P{index}",
            Start.AddHours(hour),
            "15m",
            ScanScenario.Target2R,
            PositionDirection.Long,
            ScanCardBands.Chance(2m),
            StopBand: 2,
            TargetR: 1.8m,
            outcome,
            Third,
            MarketBand: null,
            StopNatr: 1m,
            CostNatr: 0.175m);
    }
}