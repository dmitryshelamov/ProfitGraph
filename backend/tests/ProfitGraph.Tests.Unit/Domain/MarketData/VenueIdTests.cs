using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Unit.Domain.MarketData;

public sealed class VenueIdTests
{
    [TestCase("bybit")]
    [TestCase("t-invest")]
    [TestCase("venue2")]
    public void AcceptsLowercaseIdentifiers(string value)
    {
        new VenueId(value).Value.ShouldBe(value);
        new VenueId(value).ShouldBe(new VenueId(value));
    }

    [TestCase("")]
    [TestCase("Bybit")]
    [TestCase("by bit")]
    [TestCase("by_bit")]
    [TestCase("a-venue-identifier-longer-than-32")]
    public void RejectsOtherText(string value)
    {
        VenueId.IsValid(value).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => new VenueId(value));
    }

    [Test]
    public void NullIsNotAVenue()
    {
        VenueId.IsValid(null).ShouldBeFalse();
    }
}