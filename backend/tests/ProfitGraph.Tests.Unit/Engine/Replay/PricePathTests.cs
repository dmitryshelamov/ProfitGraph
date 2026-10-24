using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class PricePathTests
{
    [Test]
    public void Build_TraversesOpenLowHighCloseAndRetainsBoundaries()
    {
        Candle candle = CandleGenerator.Generate();

        var path = PricePath.Build(candle);

        path.Open.ShouldBe(candle.Open);
        path.Close.ShouldBe(candle.Close);
        path.Segments.ShouldBe(
            new[]
            {
                new PriceSegment(start: 100m, end: 90m, PricePhase.OpenToLow),
                new PriceSegment(start: 90m, end: 110m, PricePhase.LowToHigh),
                new PriceSegment(start: 110m, end: 105m, PricePhase.HighToClose)
            });
    }

    [TestCase(100, 100, 100, 100, 0)]
    [TestCase(90, 110, 90, 110, 1)]
    [TestCase(110, 110, 90, 90, 3)]
    [TestCase(100, 110, 100, 105, 2)]
    [TestCase(100, 110, 90, 110, 2)]
    public void EqualPrices_OmitOnlyZeroLengthSegments(int open, int high, int low, int close, int expectedCount)
    {
        var candle = new Candle(DateTimeOffset.UnixEpoch, open, high, low, close, Volume: 0m);

        var path = PricePath.Build(candle);

        path.Open.ShouldBe(open);
        path.Close.ShouldBe(close);
        path.Segments.Count.ShouldBe(expectedCount);
        path.Segments.ShouldAllBe(segment => segment.Start != segment.End);
    }

    [Test]
    public void Segments_CannotBeChangedByConsumers()
    {
        var path = PricePath.Build(CandleGenerator.Generate());
        var segments = (IList<PriceSegment>)path.Segments;

        Should.Throw<NotSupportedException>(() => segments.Clear());
        path.Segments.Count.ShouldBe(3);
    }

    [Test]
    public void ExactDecimalsAndMaxValue_ArePreservedWithoutComputingOverflowingDistances()
    {
        const decimal smallest = 0.0000000000000000000000000001m;
        var candle = new Candle(DateTimeOffset.UnixEpoch, Open: 1m, decimal.MaxValue, smallest, Close: 1.0000000000000000000000000001m, Volume: 0m);

        var path = PricePath.Build(candle);

        path.Segments[0].End.ShouldBe(smallest);
        path.Segments[1].End.ShouldBe(decimal.MaxValue);
        path.Close.ShouldBe(1.0000000000000000000000000001m);
    }

    [TestCase(0, 110, 0, 100)]
    [TestCase(100, 90, 110, 100)]
    [TestCase(120, 110, 90, 100)]
    [TestCase(100, 110, 90, 80)]
    public void InvalidOhlc_IsRejected(int open, int high, int low, int close)
    {
        Should.Throw<ArgumentException>(() => PricePath.Build(new Candle(DateTimeOffset.UnixEpoch, open, high, low, close, Volume: 0m)));
    }

    [TestCase(arg1: 100, arg2: 100, PricePhase.OpenToLow)]
    [TestCase(arg1: 0, arg2: 100, PricePhase.LowToHigh)]
    [TestCase(arg1: 100, arg2: 90, PricePhase.LowToHigh)]
    [TestCase(arg1: 90, arg2: 100, PricePhase.HighToClose)]
    [TestCase(arg1: 100, arg2: 90, PricePhase.Open)]
    public void InvalidSegment_IsRejected(int start, int end, PricePhase phase)
    {
        Should.Throw<ArgumentException>(() => new PriceSegment(start, end, phase));
    }
}