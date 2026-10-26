using System.Globalization;
using System.Text;

using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Median history by the observed contract (Profit Fox Designer ADR 0081, 2026-10-24 observations), with invented
///     prices and counts.
/// </summary>
public sealed class SteamPriceHistoryParserTests
{
    private static readonly SteamCurrency Kzt = SteamCurrency.Find("KZT")!;

    /// <summary>
    ///     Observed at 12:00 UTC on 2026-10-25: the hourly zone starts at 12:00 on 2026-09-25.
    /// </summary>
    private static readonly DateTimeOffset ObservedAt = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void SplitsDailyAndHourlyMediansAndEstimatesCompleteDaysInTheZone()
    {
        (string Time, decimal Median, int Sold)[] points =
        [
            ("Sep 23 2026 01: +0", 100m, 10),
            ("Sep 24 2026 01: +0", 110m, 20),
            ("Sep 25 2026 01: +0", 999m, 5),
            ("Sep 25 2026 12: +0", 120m, 1),
            .. Enumerable.Range(start: 0, count: 24).Select(hour => ($"Sep 26 2026 {hour:00}: +0", hour < 12 ? 130m : 140m, 1)),
            ("Oct 25 2026 10: +0", 150m, 2),
            ("Oct 25 2026 11: +0", 160m, 3)
        ];
        string json = History(points);

        SteamPriceHistory history = SteamPriceHistoryParser.Read(Encoding.UTF8.GetBytes(json), Kzt, ObservedAt, TimeSpan.FromSeconds(120));

        history.HourlyStart.ShouldBe(new DateTimeOffset(year: 2026, month: 9, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero));
        history.Daily.Select(candle => (candle.OpenTime.Day, candle.Close, candle.Volume))
            .ShouldBe([(23, 100m, 10m), (24, 110m, 20m), (26, 135m, 24m)]);
        history.Daily.ShouldAllBe(candle => candle.Open == candle.Close && candle.High == candle.Close && candle.Low == candle.Close);
        history.Hourly.First().OpenTime.Hour.ShouldBe(12);
        history.Hourly.Last().OpenTime.ShouldBe(new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 10, minute: 0, second: 0, TimeSpan.Zero));
        history.Hourly.Count.ShouldBe(1 + 24 + 1);
        history.For(Timeframe.Parse("1d")).ShouldBe(history.Daily);
    }

    [Test]
    public void APriceInAnotherCurrencyIsRefused()
    {
        var json = """{"success":true,"price_prefix":"$","price_suffix":"","prices":[["Sep 23 2026 01: +0",1.5,"3"]]}""";

        Should.Throw<SteamMarketDataException>(() => SteamPriceHistoryParser.Read(Encoding.UTF8.GetBytes(json), Kzt, ObservedAt, TimeSpan.Zero))
            .Unavailable.ShouldBeFalse();
    }

    [TestCase("""{"success":false,"prices":[]}""")]
    [TestCase("""{"success":true,"price_prefix":"","price_suffix":"₸","prices":[["Sep 24 2026 01: +0",1,"3"],["Sep 23 2026 01: +0",1,"3"]]}""")]
    [TestCase("""{"success":true,"price_prefix":"","price_suffix":"₸","prices":[["Sep 23 2026 01: +0",1,"2.5"]]}""")]
    [TestCase("""{"success":true,"price_prefix":"","price_suffix":"₸","prices":[["Sep 23 2026 01: +0",0,"3"]]}""")]
    [TestCase("""{"success":true,"price_prefix":"","price_suffix":"₸","prices":[["23.09.2026",1,"3"]]}""")]
    [TestCase("not json")]
    public void AnythingMalformedIsRefused(string json)
    {
        Should.Throw<SteamMarketDataException>(() => SteamPriceHistoryParser.Read(Encoding.UTF8.GetBytes(json), Kzt, ObservedAt, TimeSpan.Zero));
    }

    [Test]
    public void ReadsTheWalletOfTheMarketPage()
    {
        var html = $"<script>var g_rgWalletInfo = {new MockSteam().WalletInfo};</script>";

        SteamWallet wallet = SteamWalletParser.Read(Encoding.UTF8.GetBytes(html));

        wallet.Currency.ShouldBe(Kzt);
        wallet.Balance.ShouldBe(1234.56m);
        wallet.SellerFees.Rates.ShouldBe([0.05m, 0.10m]);
        wallet.SellerFees.Minimum.ShouldBe(0.01m);
        wallet.SellerFees.Increment.ShouldBe(1);
    }

    [Test]
    public void KeepsAWalletCurrencyIncrementOfWholeTenge()
    {
        string info = new MockSteam().WalletInfo.Replace("\"wallet_currency_increment\":1", "\"wallet_currency_increment\":100", StringComparison.Ordinal);

        SteamWallet wallet = SteamWalletParser.Read(Encoding.UTF8.GetBytes($"<script>var g_rgWalletInfo = {info};</script>"));

        wallet.SellerFees.Increment.ShouldBe(100);
        wallet.SellerFees.BuyerPrice(sellerAmount: 1m, Kzt.Precision).ShouldBe(3m);
    }

    [Test]
    public void AWalletWithoutAnIncrementStepsInMinorUnits()
    {
        string info = new MockSteam().WalletInfo.Replace(",\"wallet_currency_increment\":1", "", StringComparison.Ordinal);

        SteamWalletParser.Read(Encoding.UTF8.GetBytes($"<script>var g_rgWalletInfo = {info};</script>")).SellerFees.Increment.ShouldBe(1);
    }

    [TestCase("\"wallet_currency\":37", "\"wallet_currency\":99")]
    [TestCase("\"wallet_currency_increment\":1", "\"wallet_currency_increment\":0")]
    [TestCase("\"wallet_currency_increment\":1", "\"wallet_currency_increment\":1.5")]
    [TestCase("\"wallet_fee_percent\":\"0.05\",", "")]
    public void AnUnsupportedOrIncompleteWalletIsRefused(string field, string replacement)
    {
        var html = $"<script>var g_rgWalletInfo = {new MockSteam().WalletInfo.Replace(field, replacement, StringComparison.Ordinal)};</script>";

        Should.Throw<SteamMarketDataException>(() => SteamWalletParser.Read(Encoding.UTF8.GetBytes(html)));
    }

    private static string History((string Time, decimal Median, int Sold)[] points)
    {
        return """{"success":true,"price_prefix":"","price_suffix":"₸","prices":[""" +
            string.Join(separator: ',', points.Select(point => string.Create(CultureInfo.InvariantCulture, $"[\"{point.Time}\",{point.Median},\"{point.Sold}\"]"))) +
            "]}";
    }
}