using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Engine.Market;

public sealed class CandleValidatorTests
{
    [TestCase(100, 110, 90, 105, 1, true)]
    [TestCase(100, 99, 90, 105, 1, false)]
    [TestCase(100, 110, 101, 105, 1, false)]
    [TestCase(0, 110, 0, 105, 1, false)]
    [TestCase(100, 110, 90, 105, -1, false)]
    public void ValidatesPrices(int open, int high, int low, int close, int volume, bool expected)
    {
        var candle = new Candle(DateTimeOffset.UnixEpoch, open, high, low, close, volume);

        CandleValidator.IsValid(candle, Timeframe.Parse("1h")).ShouldBe(expected);
    }

    [Test]
    public void RejectsMisalignedAndNonUtcTime()
    {
        var candle = new Candle(DateTimeOffset.UnixEpoch.AddMinutes(1), Open: 100, High: 110, Low: 90, Close: 105, Volume: 1);

        CandleValidator.IsValid(candle, Timeframe.Parse("1h")).ShouldBeFalse();
        CandleValidator.IsValid(
                candle with { OpenTime = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(3)) },
                Timeframe.Parse("1h"))
            .ShouldBeFalse();
    }

    [TestCase("1m", arg2: 1)]
    [TestCase("1h", arg2: 60)]
    [TestCase("1d", arg2: 1440)]
    public void ParsesSupportedTimeframes(string key, int minutes)
    {
        Timeframe.Parse(key).Duration.ShouldBe(TimeSpan.FromMinutes(minutes));
    }

    [Test]
    public void RejectsUnsupportedTimeframe()
    {
        Should.Throw<ArgumentException>(() => Timeframe.Parse("7m"));
    }
}