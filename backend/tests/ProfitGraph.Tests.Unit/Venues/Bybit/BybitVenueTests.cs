using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
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

    [Test]
    public void FuturesTakeTheSpotKey()
    {
        var futures = new BybitLinearVenue();
        var registry = new VenueRegistry([_venue, futures]);

        ((IVenueDescriptor)_venue).CredentialVenue.ShouldBeNull();
        futures.CredentialVenue.ShouldBe(_venue.Id);
        registry.AcceptsCredential(futures.Id, _venue.Id).ShouldBeTrue();
        registry.AcceptsCredential(futures.Id, futures.Id).ShouldBeTrue();
        registry.AcceptsCredential(_venue.Id, futures.Id).ShouldBeFalse();
    }

    [Test]
    public void OnlySpotEnrollsAKeyWhileFuturesKeysEnrolledEarlierKeepTheirAccountCards()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddBybitVenue(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetServices<IVenueCredentialSchema>().Select(schema => schema.Venue).ShouldBe([_venue.Id]);
        provider.GetServices<IVenueAccountIdentityReader>().Select(reader => reader.Venue.Value).ShouldBe(["bybit", "bybit-linear"]);
        provider.GetServices<IAccountBalanceReader>().Select(reader => reader.Venue.Value).ShouldBe(["bybit", "bybit-linear"]);
    }
}