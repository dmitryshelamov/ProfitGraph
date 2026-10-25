using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Symbols of Steam items with invented App IDs and names, except the real case of the owner's ladder (ADR 0022).
/// </summary>
public sealed class SteamVenueTests
{
    private readonly SteamVenue _venue = new SteamVenue();

    [TestCase("KZT:730:Dreams & Nightmares Case", "KZT:730:Dreams & Nightmares Case")]
    [TestCase("kzt:730:Dreams & Nightmares Case", "KZT:730:Dreams & Nightmares Case")]
    [TestCase(" usd : 0480 :Example/Blue: Mk II", "USD:480:Example/Blue: Mk II")]
    [TestCase("EUR:480:StatTrak™ ★ Example | Doppler (Factory New)", "EUR:480:StatTrak™ ★ Example | Doppler (Factory New)")]
    public void KeepsTheHashNameExactly(string input, string expected)
    {
        _venue.TryParseSymbol(input, out InstrumentSymbol? symbol).ShouldBeTrue();
        symbol.ShouldBe(new InstrumentSymbol(expected));
    }

    [Test]
    public void TellsHashNamesApartByCase()
    {
        _venue.TryParseSymbol("USD:480:Example/Blue: Mk II", out InstrumentSymbol? upper).ShouldBeTrue();
        _venue.TryParseSymbol("USD:480:Example/blue: Mk II", out InstrumentSymbol? lower).ShouldBeTrue();
        upper.ShouldNotBe(lower);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("730:Dreams & Nightmares Case")]
    [TestCase("GBP:730:Dreams & Nightmares Case")]
    [TestCase("KZT:0:Dreams & Nightmares Case")]
    [TestCase("KZT:-730:Dreams & Nightmares Case")]
    [TestCase("KZT:7a0:Dreams & Nightmares Case")]
    [TestCase("KZT:99999999999:Dreams & Nightmares Case")]
    [TestCase("KZT:730:")]
    [TestCase("KZT:730: Dreams & Nightmares Case")]
    [TestCase("KZT:730:Dreams & Nightmares Case ")]
    [TestCase("KZT:730:Dreams  & Nightmares Case")]
    [TestCase("KZT:730:Dreams\t& Nightmares Case")]
    [TestCase("KZT:730:Dreams\u0007Case")]
    public void RejectsWhatIsNotASteamItem(string? input)
    {
        _venue.TryParseSymbol(input, out InstrumentSymbol? symbol).ShouldBeFalse();
        symbol.ShouldBeNull();
    }

    [Test]
    public void RejectsANameLongerThanASymbol()
    {
        _venue.TryParseSymbol($"KZT:730:{new string(c: 'x', InstrumentSymbol.MaxLength)}", out _).ShouldBeFalse();
    }

    [Test]
    public void DescribesItself()
    {
        _venue.Id.ShouldBe(new VenueId("steam"));
        _venue.Name.ShouldBe("Steam Community Market");
        _venue.Timeframes.Select(timeframe => timeframe.Key).ShouldBe(["1h", "1d"]);
    }

    [Test]
    public void SupportsOnlyWalletCurrenciesWithKnownMinorUnits()
    {
        SteamCurrency.Supported.Select(currency => currency.Code).ShouldBe(["USD", "EUR", "RUB", "KZT"]);
        SteamCurrency.Find("kzt")!.SteamId.ShouldBe(37);
        SteamCurrency.Find("KZT")!.Precision.ShouldBe(2);
        SteamCurrency.Find("GBP").ShouldBeNull();
    }

    [Test]
    public void PrefillsBacktestsWithSteamAndPublisherFees()
    {
        var defaults = new SteamExecutionDefaults();

        defaults.Venue.ShouldBe(new VenueId("steam"));
        defaults.Defaults.SellerFees!.Rates.ShouldBe([0.05m, 0.10m]);
        defaults.Defaults.SellerFees.Minimum.ShouldBe(0.01m);
        defaults.Defaults.SettlementHoldSeconds.ShouldBe(604_800);
    }
}