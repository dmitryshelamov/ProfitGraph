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
        Enabled(origin).IsValid().ShouldBeTrue();
    }

    [Test]
    public void EveryHostNeedsItsOwnOfficialName()
    {
        new SteamOptions
            {
                Enabled = true,
                CommunityBaseAddress = "https://steamcommunity.com/",
                LoginBaseAddress = "https://login.steampowered.com/",
                ApiBaseAddress = "https://api.steampowered.com/"
            }
            .IsValid()
            .ShouldBeTrue();
        new SteamOptions
            {
                Enabled = true,
                CommunityBaseAddress = "https://steamcommunity.com/",
                LoginBaseAddress = "https://steamcommunity.com/",
                ApiBaseAddress = "https://api.steampowered.com/"
            }
            .IsValid()
            .ShouldBeFalse();
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
        Enabled(origin).IsValid().ShouldBeFalse();
    }

    [Test]
    public void RefusesABadOriginEvenWhenDisabled()
    {
        new SteamOptions { CommunityBaseAddress = "https://proxy.example.net/" }.IsValid().ShouldBeFalse();
    }

    [Test]
    public void BoundsTheBuyPolicies()
    {
        new SteamOptions().ShouldSatisfyAllConditions(
            options => options.DefiniteRejectionCodes.ShouldBeEmpty(),
            options => options.MarketBuySlippage.ShouldBe(0m),
            options => options.MarketBuyDeadline.ShouldBe(TimeSpan.FromMinutes(2)));
        new SteamOptions { MarketBuySlippage = 0.11m }.IsValid().ShouldBeFalse();
        new SteamOptions { MarketBuySlippage = -0.01m }.IsValid().ShouldBeFalse();
        new SteamOptions { MarketBuyDeadline = TimeSpan.Zero }.IsValid().ShouldBeFalse();
        new SteamOptions { DefiniteRejectionCodes = [1] }.IsValid().ShouldBeFalse();
        new SteamOptions { DefiniteRejectionCodes = [29], MarketBuySlippage = 0.1m }.IsValid().ShouldBeTrue();
    }

    private static SteamOptions Enabled(string community)
    {
        return new SteamOptions
        {
            Enabled = true,
            CommunityBaseAddress = community,
            LoginBaseAddress = "https://login.mock-steam.invalid/",
            ApiBaseAddress = "https://api.mock-steam.invalid/"
        };
    }
}