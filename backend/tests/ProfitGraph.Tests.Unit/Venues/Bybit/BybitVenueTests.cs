using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Bybit;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

public sealed class BybitVenueTests
{
    private readonly BybitVenue _venue = new BybitVenue();

    [TestCase("BTCUSDT")]
    [TestCase("btcusdt")]
    [TestCase(" btc/usdt ")]
    public void TurnsTypedPairsIntoBybitSymbols(string input)
    {
        _venue.TryParseSymbol(input, out InstrumentSymbol? symbol).ShouldBeTrue();
        symbol.ShouldBe(new InstrumentSymbol("BTCUSDT"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("BTC-USDT")]
    [TestCase("BTC USDT")]
    [TestCase("ЁЖUSDT")]
    [TestCase("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void RejectsWhatIsNotABybitSymbol(string? input)
    {
        _venue.TryParseSymbol(input, out InstrumentSymbol? symbol).ShouldBeFalse();
        symbol.ShouldBeNull();
    }

    [Test]
    public void DescribesItself()
    {
        _venue.Id.ShouldBe(new VenueId("bybit"));
        _venue.Name.ShouldBe("Bybit");
        _venue.Timeframes.Select(timeframe => timeframe.Key).ShouldBe(["1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d"]);
    }
}