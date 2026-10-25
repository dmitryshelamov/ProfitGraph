using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Venues.Bybit.LiveData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     Bybit's public Spot ticker messages give the display price and the message time (S5-19).
/// </summary>
public sealed class BybitTickerMessagesTests
{
    private const string Topic = "tickers.BTCUSDT";

    [Test]
    public void TickerMessageGivesTheLastPriceAtTheMessageTime()
    {
        LiveMarkPriceInternal? price = BybitTickerMessages.Price(
            message: """{"topic":"tickers.BTCUSDT","ts":1793000000000,"type":"snapshot","data":{"symbol":"BTCUSDT","lastPrice":"68123.45"}}""",
            Topic);

        price.ShouldBe(new LiveMarkPriceInternal(Price: 68123.45m, DateTimeOffset.FromUnixTimeMilliseconds(1_793_000_000_000)));
    }

    [TestCase("""{"topic":"tickers.ETHUSDT","ts":1793000000000,"data":{"lastPrice":"1"}}""")]
    [TestCase("""{"op":"subscribe","success":true}""")]
    [TestCase("""{"topic":"tickers.BTCUSDT","ts":1793000000000,"data":{"lastPrice":"0"}}""")]
    [TestCase("""{"topic":"tickers.BTCUSDT","data":{"lastPrice":"1"}}""")]
    [TestCase("not json")]
    public void OtherOrInvalidMessagesGiveNoPrice(string message)
    {
        BybitTickerMessages.Price(message, Topic).ShouldBeNull();
    }
}