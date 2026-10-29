using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Scanner.Ports;
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

    [TestCase("false", "8d3f0b8e-6a8f-4b55-9c43-3c8d9e1d6a10", arg3: false)]
    [TestCase("true", "", arg3: false)]
    [TestCase("true", "8d3f0b8e-6a8f-4b55-9c43-3c8d9e1d6a10", arg3: true)]
    public void TheScannerExistsOnlyWhileTheVenueIsEnabledWithTheScannersCredential(string enabled, string credentialId, bool scanned)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Venues:TInvest:Enabled"] = enabled,
                    ["Venues:TInvest:Endpoint"] = "https://mock-tinvest.invalid/",
                    ["Venues:TInvest:Scanner:CredentialId"] = credentialId
                })
            .Build();
        IServiceCollection services = new ServiceCollection().AddTInvestVenue(configuration);

        services.Any(service => service.ServiceType == typeof(IMarketScanSource)).ShouldBe(scanned);
        services.Any(service => service.ServiceType == typeof(IScanMarketDataAccess)).ShouldBe(scanned);
    }

    [TestCase(arg1: 50, arg2: 50, arg3: true)]
    [TestCase(arg1: 51, arg2: 50, arg3: false)]
    [TestCase(arg1: 50, arg2: 101, arg3: false)]
    [TestCase(arg1: 0, arg2: 50, arg3: false)]
    public void AScannerStreamCarriesNoMoreSharesThanAMinutesSubscriptions(int sharesPerStream, int subscriptionsPerMinute, bool valid)
    {
        new TInvestOptions { Scanner = new TInvestScannerOptions { SharesPerStream = sharesPerStream, SubscriptionsPerMinute = subscriptionsPerMinute } }
            .IsValid()
            .ShouldBe(valid);
    }
}