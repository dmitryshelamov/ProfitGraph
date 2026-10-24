using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class OutOfSampleSplitTests
{
    [Test]
    public void DefaultHoldsOutLastTwentyPercent()
    {
        SampleRanges ranges = OutOfSampleSplit.Default.Split(1000);

        OutOfSampleSplit.Default.Fraction.ShouldBe(0.2m);
        ranges.InSampleBars.ShouldBe(800);
        ranges.OutOfSampleBars.ShouldBe(200);
    }

    [TestCase(arg1: 10, arg2: 8, arg3: 2)]
    [TestCase(arg1: 9, arg2: 8, arg3: 1)]
    [TestCase(arg1: 5, arg2: 4, arg3: 1)]
    [TestCase(arg1: 14, arg2: 12, arg3: 2)]
    public void RoundsHoldoutDownByBarCount(int barCount, int expectedInSample, int expectedOutOfSample)
    {
        SampleRanges ranges = OutOfSampleSplit.Default.Split(barCount);

        ranges.InSampleBars.ShouldBe(expectedInSample);
        ranges.OutOfSampleBars.ShouldBe(expectedOutOfSample);
    }

    [Test]
    public void RangesSliceCandlesWithoutOverlap()
    {
        int[] bars = Enumerable.Range(start: 0, count: 10).ToArray();
        SampleRanges ranges = OutOfSampleSplit.Default.Split(bars.Length);

        bars[ranges.InSample].ShouldBe([0, 1, 2, 3, 4, 5, 6, 7]);
        bars[ranges.OutOfSample].ShouldBe([8, 9]);
    }

    [Test]
    public void NoneKeepsWholeRangeInSample()
    {
        SampleRanges ranges = OutOfSampleSplit.None.Split(7);

        ranges.InSampleBars.ShouldBe(7);
        ranges.OutOfSampleBars.ShouldBe(0);
    }

    [TestCase(4)]
    [TestCase(1)]
    [TestCase(0)]
    public void RejectsRangeTooShortForBothParts(int barCount)
    {
        Should.Throw<SampleSplitException>(() => OutOfSampleSplit.Default.Split(barCount));
    }

    [TestCase(-0.1)]
    [TestCase(1)]
    [TestCase(1.5)]
    public void RejectsFractionOutsideUnitInterval(decimal fraction)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OutOfSampleSplit.Create(fraction));
    }
}