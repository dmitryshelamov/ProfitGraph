using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     A ticker displaces the last candle's close only while it is later than the close and at most five minutes old
///     (S5-19).
/// </summary>
public sealed class LiveMarkPriceTests
{
    private static readonly DateTimeOffset ClosedAt = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [TestCase(arg1: 30, arg2: 60, arg3: true)]
    [TestCase(arg1: 30, arg2: 330, arg3: true)]
    [TestCase(arg1: 30, arg2: 331, arg3: false)]
    [TestCase(arg1: 0, arg2: 10, arg3: false)]
    [TestCase(arg1: -30, arg2: 10, arg3: false)]
    public void TickerDisplacesTheCandleWhileFresh(int observedAfterCloseSeconds, int nowAfterCloseSeconds, bool displaces)
    {
        var ticker = new LiveMarkPrice(new VenueId("bybit"), new InstrumentSymbol("BTCUSDT"), Price: 100m, ClosedAt.AddSeconds(observedAfterCloseSeconds));

        ticker.Displaces(ClosedAt, ClosedAt.AddSeconds(nowAfterCloseSeconds)).ShouldBe(displaces);
    }
}