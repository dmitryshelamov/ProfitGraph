using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.TInvest;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

public sealed class TInvestVenueTests
{
    [TestCase("TQBR:SBER", "TQBR:SBER")]
    [TestCase(" sber ", "TQBR:SBER")]
    [TestCase("tqbr:gazp", "TQBR:GAZP")]
    [TestCase("SBERP", "TQBR:SBERP")]
    public void ASymbolIsAShareOfTheMainBoard(string input, string expected)
    {
        new TInvestVenue().TryParseSymbol(input, out InstrumentSymbol? symbol).ShouldBeTrue();

        symbol!.Value.ShouldBe(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("TQTF:TMOS")]
    [TestCase("SBER_TQBR")]
    [TestCase("BTC/USDT")]
    [TestCase("TQBR:")]
    [TestCase("TOOLONGTICKER1")]
    public void OtherInputIsNotASymbol(string? input)
    {
        new TInvestVenue().TryParseSymbol(input, out InstrumentSymbol? symbol).ShouldBeFalse();

        symbol.ShouldBeNull();
    }

    [Test]
    public void TheInvestApiNamesTheShareTickerFirst()
    {
        TInvestSymbol.TryParse("TQBR:SBER", out TInvestSymbol? symbol).ShouldBeTrue();

        symbol!.InstrumentId.ShouldBe("SBER_TQBR");
    }

    [Test]
    public void TheVenueOffersIntradayTimeframesOnly()
    {
        new TInvestVenue().Timeframes.Select(timeframe => timeframe.Key).ShouldBe(["1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h"]);
    }

    [TestCase(arg1: false, "", arg3: true)]
    [TestCase(arg1: true, "", arg3: false)]
    [TestCase(arg1: true, "https://invest-public-api.tbank.ru", arg3: true)]
    [TestCase(arg1: true, "https://invest-public-api.tbank.ru:443/", arg3: true)]
    [TestCase(arg1: true, "https://mock-tinvest.invalid", arg3: true)]
    [TestCase(arg1: true, "http://invest-public-api.tbank.ru", arg3: false)]
    [TestCase(arg1: true, "https://invest-public-api.tbank.ru/path", arg3: false)]
    [TestCase(arg1: true, "https://example.com", arg3: false)]
    public void OnlyTheOfficialHttpsEndpointEnablesTheVenue(bool enabled, string endpoint, bool valid)
    {
        new TInvestOptions { Enabled = enabled, Endpoint = endpoint }.IsValid().ShouldBe(valid);
    }
}