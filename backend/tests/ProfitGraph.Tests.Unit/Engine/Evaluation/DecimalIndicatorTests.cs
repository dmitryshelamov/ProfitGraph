using System.Globalization;

using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Tests.Unit.Engine.Evaluation;

public sealed class DecimalIndicatorTests
{
    [Test]
    public void EmaSeedsWithTheSimpleMeanAndThenSmoothsWithTwoOverPeriodPlusOne()
    {
        var ema = new ExponentialMovingAverage(period: 3);

        // alpha = 2 / 4: seed (1 + 2 + 3) / 3 = 2, then (6 + 2) / 2 = 4, (4 + 4) / 2 = 4, (10 + 4) / 2 = 7.
        Feed(ema.Add, 1m, 2m, 3m, 6m, 4m, 10m).ShouldBe([null, null, 2m, 4m, 4m, 7m]);
    }

    [Test]
    public void EmaKeepsDecimalPrecisionOfANonTerminatingAlpha()
    {
        var ema = new ExponentialMovingAverage(period: 2);

        // Seed 1.5, then (2 · 2 + 1 · 1.5) / 3 = 11 / 6.
        Feed(ema.Add, 1m, 2m, 2m).ShouldBe([null, 1.5m, 1.8333333333333333333333333333m]);
    }

    [Test]
    public void EmaOfPeriodOneIsTheSample()
    {
        var ema = new ExponentialMovingAverage(period: 1);

        Feed(ema.Add, 5m, 7m, 3m).ShouldBe([5m, 7m, 3m]);
    }

    [Test]
    public void UnavailableSampleRestartsTheEmaSeed()
    {
        var ema = new ExponentialMovingAverage(period: 2);

        Feed(ema.Add, 1m, 3m, null, 10m, 20m, 20m).ShouldBe([null, 2m, null, null, 15m, 55m / 3m]);
    }

    [Test]
    public void RsiUsesWilderSmoothingAfterTheSimpleSeed()
    {
        var rsi = new WilderRelativeStrength(period: 2);

        // Changes +2, −1, +3, 0, −1. Seed G = 1, L = 0.5 → 200/3; then G = 2, L = 0.25 → 800/9; G = 1, L = 0.125 → 800/9;
        // G = 0.5, L = 0.5625 → 800/17.
        Feed(rsi.Add, 10m, 12m, 11m, 14m, 14m, 13m)
            .ShouldBe(
            [
                null, null, 66.666666666666666666666666667m, 88.888888888888888888888888889m, 88.888888888888888888888888889m,
                47.058823529411764705882352941m
            ]);
    }

    [Test]
    public void RsiWithoutLossesIsHundredWithoutGainsIsZeroAndFlatIsNeutral()
    {
        Feed(new WilderRelativeStrength(period: 2).Add, 1m, 2m, 3m, 4m).ShouldBe([null, null, 100m, 100m]);
        Feed(new WilderRelativeStrength(period: 2).Add, 4m, 3m, 2m, 1m).ShouldBe([null, null, 0m, 0m]);
        Feed(new WilderRelativeStrength(period: 2).Add, 5m, 5m, 5m, 5m).ShouldBe([null, null, 50m, 50m]);
    }

    [Test]
    public void UnavailableSampleRestartsTheRsiHistory()
    {
        var rsi = new WilderRelativeStrength(period: 1);

        Feed(rsi.Add, 1m, 2m, null, 5m, 4m, 6m).ShouldBe([null, 100m, null, null, 0m, 100m]);
    }

    [Test]
    public void StandardDeviationIsThePopulationDeviationOfTheWindow()
    {
        var deviation = new RollingStandardDeviation(period: 4);

        // Window 2, 4, 4, 4: mean 3.5, squares 3, variance 0.75; then 4, 4, 4, 5: mean 4.25, squares 0.75, variance 0.1875.
        Feed(deviation.Add, 2m, 4m, 4m, 4m, 5m)
            .ShouldBe([null, null, null, 0.8660254037844386467637231708m, 0.4330127018922193233818615854m]);
    }

    [Test]
    public void FlatWindowHasZeroDeviationAndUnavailableSampleEmptiesTheWindow()
    {
        var deviation = new RollingStandardDeviation(period: 2);

        Feed(deviation.Add, 7.25m, 7.25m, 1m, null, 3m, 5m).ShouldBe([null, 0m, 3.125m, null, null, 1m]);
    }

    [TestCase("0", "0")]
    [TestCase("4", "2")]
    [TestCase("0.0001", "0.01")]
    [TestCase("144", "12")]
    [TestCase("2", "1.4142135623730950488016887242")]
    [TestCase("0.75", "0.8660254037844386467637231708")]
    [TestCase("1000000000000", "1000000")]
    public void DecimalSquareRootIsExactForSquaresAndTwentyEightDigitsOtherwise(string value, string root)
    {
        DecimalSquareRoot.Sqrt(decimal.Parse(value, CultureInfo.InvariantCulture))
            .ShouldBe(decimal.Parse(root, CultureInfo.InvariantCulture));
    }

    [Test]
    public void DecimalSquareRootStaysWithinTheLastDigitsAcrossTheDecimalRange()
    {
        foreach (decimal value in new[] { 0.0000000000000000000000000001m, 0.5m, 3m, 98765.4321m, 79228162514264337593543950335m })
        {
            decimal root = DecimalSquareRoot.Sqrt(value);
            decimal error = decimal.Abs(root - value / root) / root;

            error.ShouldBeLessThan(0.000000000000000000000001m);
        }

        Should.Throw<ArgumentOutOfRangeException>(() => DecimalSquareRoot.Sqrt(-1m));
    }

    private static decimal?[] Feed(Func<decimal?, decimal?> add, params decimal?[] samples)
    {
        return samples.Select(add).ToArray();
    }
}