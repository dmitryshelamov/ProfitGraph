using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Unit.Application.MarketData;

public sealed class VenueRegistryTests
{
    private readonly VenueRegistry _registry = new VenueRegistry([new TestVenue(), new TestVenue("other")]);

    [Test]
    public void ResolvesASeriesThroughTheVenueRules()
    {
        CandleSeries series = _registry.ResolveSeries("other", "ABC", " 1d ");

        series.Venue.ShouldBe(new VenueId("other"));
        series.Symbol.ShouldBe(new InstrumentSymbol("ABC"));
        series.Timeframe.Key.ShouldBe("1d");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("TEST")]
    [TestCase("missing")]
    public void UnknownVenueIsNeverReplacedByADefault(string? venue)
    {
        _registry.Find(venue).ShouldBeNull();
        _registry.TryResolveSeries(venue, "ABC", "1h", out CandleSeries? series).Single().Code.ShouldBe("unknown_venue");
        series.ShouldBeNull();
    }

    [Test]
    public void ReportsEveryWrongPartOfAKnownVenue()
    {
        _registry.TryResolveSeries("test", "abc", "4h", out CandleSeries? series).Select(error => error.Code).ShouldBe(["invalid_symbol", "unsupported_timeframe"]);
        series.ShouldBeNull();
        Should.Throw<ApplicationValidationException>(() => _registry.ResolveSeries("test", "abc", "1h")).Errors.Single().Field.ShouldBe("Symbol");
    }

    [Test]
    public void TwoModulesCannotClaimOneVenue()
    {
        Should.Throw<InvalidOperationException>(() => new VenueRegistry([new TestVenue(), new TestVenue()]));
    }
}