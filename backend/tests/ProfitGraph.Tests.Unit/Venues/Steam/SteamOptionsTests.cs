using ProfitGraph.Venues.Steam;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

public sealed class SteamOptionsTests
{
    [Test]
    public void IsOffWithoutAnOriginByDefault()
    {
        var options = new SteamOptions();

        options.Enabled.ShouldBeFalse();
        options.CommunityBaseAddress.ShouldBeEmpty();
        options.IsValid().ShouldBeTrue();
    }

    [TestCase("https://steamcommunity.com/")]
    [TestCase("https://steamcommunity.com")]
    [TestCase("https://mock-steam.invalid/")]
    public void AcceptsTheOfficialOriginOrAnInvalidTestHost(string origin)
    {
        new SteamOptions { Enabled = true, CommunityBaseAddress = origin }.IsValid().ShouldBeTrue();
    }

    [TestCase("")]
    [TestCase("http://steamcommunity.com/")]
    [TestCase("https://steamcommunity.com:8443/")]
    [TestCase("https://user:secret@steamcommunity.com/")]
    [TestCase("https://steamcommunity.com/market/")]
    [TestCase("https://steamcommunity.com/?l=english")]
    [TestCase("https://steamcommunity.com.example.net/")]
    [TestCase("https://proxy.example.net/")]
    public void RefusesAnyOtherOriginWhenEnabled(string origin)
    {
        new SteamOptions { Enabled = true, CommunityBaseAddress = origin }.IsValid().ShouldBeFalse();
    }

    [Test]
    public void RefusesABadOriginEvenWhenDisabled()
    {
        new SteamOptions { CommunityBaseAddress = "https://proxy.example.net/" }.IsValid().ShouldBeFalse();
    }
}