using System.Globalization;
using System.Net;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     History, item search and rules through the market-data account's session, against the mock Steam.
/// </summary>
public sealed class SteamMarketDataTests
{
    private static readonly InstrumentSymbol Case = new InstrumentSymbol("KZT:730:Dreams & Nightmares Case");
    private static readonly Timeframe Day = Timeframe.Parse("1d");

    [Test]
    public async Task CallersShareOneReadOfAnItemsHistory()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.PriceHistory =
            """{"success":true,"price_prefix":"","price_suffix":"₸","prices":[["Sep 01 2026 01: +0",500.5,"1200"],["Sep 02 2026 01: +0",510,"1100"]]}""";
        var source = new SteamHistoricalCandleSource(harness.MarketData());

        IReadOnlyList<Candle>[] reads = await Task.WhenAll(
            Enumerable.Range(start: 0, count: 4).Select(_ => source.Get(Case, Day, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None)));
        DateTimeOffset? first = await source.FindFirst(Case, Day, DateTimeOffset.MaxValue, CancellationToken.None);

        reads.ShouldAllBe(candles => candles.Count == 2);
        reads[0][0]
            .ShouldBe(
                new Candle(
                    new DateTimeOffset(year: 2026, month: 9, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero),
                    Open: 500.5m,
                    High: 500.5m,
                    Low: 500.5m,
                    Close: 500.5m,
                    Volume: 1200m));
        first.ShouldBe(reads[0][0].OpenTime);
        harness.Steam.Count("/market/pricehistory/").ShouldBe(1);
        harness.Steam.Count("/market/").ShouldBe(1);
    }

    [Test]
    public async Task AnItemInAnotherCurrencyThanTheWalletHasNoHistory()
    {
        var harness = new SteamSessionHarness();
        var source = new SteamHistoricalCandleSource(harness.MarketData());

        var refused = await Should.ThrowAsync<HistoricalDataException>(() => source.Get(
            new InstrumentSymbol("USD:730:Dreams & Nightmares Case"),
            Day,
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            CancellationToken.None));

        refused.Code.ShouldBe(HistoricalDataErrorCodes.InvalidProviderData);
        harness.Steam.Count("/market/pricehistory/").ShouldBe(0);
    }

    [Test]
    public async Task WithoutAMarketDataAccountSteamHasNoHistory()
    {
        var harness = new SteamSessionHarness();
        harness.Options = new SteamOptions
        {
            Enabled = true,
            CommunityBaseAddress = MockSteam.Community,
            LoginBaseAddress = MockSteam.Login,
            ApiBaseAddress = MockSteam.Api
        };

        var refused = await Should.ThrowAsync<HistoricalDataException>(() =>
            new SteamHistoricalCandleSource(harness.MarketData()).Get(Case, Day, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None));

        refused.Code.ShouldBe(HistoricalDataErrorCodes.ProviderUnavailable);
        harness.Steam.Count("/jwt/finalizelogin").ShouldBe(0);
    }

    [TestCase("dreams", "730")]
    [TestCase("570: arcana", "570")]
    public async Task SearchFindsItemsInTheWalletsCurrency(string query, string appId)
    {
        var harness = new SteamSessionHarness();
        harness.Steam.SearchResults = """{"success":true,"results":[{"name":"Dreams & Nightmares Case","hash_name":"Dreams & Nightmares Case"},{"hash_name":"  "}]}""";
        harness.Steam.Override = request => request.RequestUri!.AbsolutePath == "/market/search/render/" &&
            !request.RequestUri.Query.Contains(string.Create(CultureInfo.InvariantCulture, $"appid={appId}&"), StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                : null;
        var catalog = new SteamInstrumentCatalog(harness.MarketData(), Options.Create(harness.Options));

        IReadOnlyList<InstrumentInternal> found = await catalog.Search(query, limit: 10, CancellationToken.None);

        found.ShouldBe([new InstrumentInternal("steam", $"KZT:{appId}:Dreams & Nightmares Case", "Dreams & Nightmares Case", "KZT")]);
    }

    [Test]
    public async Task EveryItemTradesWholeAtTheWalletsMinorUnit()
    {
        InstrumentRulesDefinitionInternal definition = await new SteamInstrumentRulesSource().Get(Case, CancellationToken.None);

        definition.ShouldSatisfyAllConditions(
            () => definition.BaseAsset.ShouldBe("Dreams & Nightmares Case"),
            () => definition.QuoteAsset.ShouldBe("KZT"),
            () => definition.Rules.QuantityStep.ShouldBe(1m),
            () => definition.Rules.MinQuantity.ShouldBe(1m),
            () => definition.Rules.PriceTick.ShouldBe(0.01m),
            () => definition.Rules.MinNotional.ShouldBe(0.03m),
            () => definition.Rules.BasePrecision.ShouldBe(0),
            () => definition.Rules.QuotePrecision.ShouldBe(2));
    }
}