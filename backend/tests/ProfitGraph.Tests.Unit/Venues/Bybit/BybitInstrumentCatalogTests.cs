using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Venues.Bybit.Instruments;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     Bybit's tradable Spot pairs, read once per list lifetime from the public endpoint, against a local stub.
/// </summary>
public sealed class BybitInstrumentCatalogTests
{
    private static readonly BybitOptions FastRetries = new BybitOptions
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        MaxRetryAttempts = 1,
        AttemptTimeout = TimeSpan.FromSeconds(5),
        TotalTimeout = TimeSpan.FromSeconds(10),
        InstrumentListLifetime = TimeSpan.FromMinutes(10)
    };

    private ManualClock _clock = null!;
    private BybitInstrumentList _list = null!;

    [SetUp]
    public void CreateList()
    {
        _clock = new ManualClock(new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero));
        _list = new BybitInstrumentList(_clock, FastRetries.InstrumentListLifetime);
    }

    [Test]
    public async Task ListsOnlyTradingPairsWhoseAssetsSpellTheSymbol()
    {
        var handler = new BybitHttpStub(_ => Response(
            Row("ETHUSDT", "ETH", "USDT"),
            Row("BTCUSDT", "BTC", "USDT"),
            Row("OLDUSDT", "OLD", "USDT", "Closed"),
            Row("ODDUSDT", "ODD", "USDC"),
            Row("BTCUSDT", "BTC", "USDT")));
        using BybitInstrumentCatalog catalog = Catalog(handler);

        IReadOnlyList<InstrumentInternal> instruments = await catalog.Search(string.Empty, limit: 10, CancellationToken.None);

        instruments.ShouldBe([new InstrumentInternal("bybit", "BTCUSDT", "BTC", "USDT"), new InstrumentInternal("bybit", "ETHUSDT", "ETH", "USDT")]);
        Uri request = handler.Requests.Single();
        request.AbsolutePath.ShouldBe("/v5/market/instruments-info");
        request.Query.ShouldContain("category=spot");
        request.Query.ShouldNotContain("symbol=");
    }

    [Test]
    public async Task SearchPutsExactAndPrefixMatchesFirst_AndAcceptsASlash()
    {
        using BybitInstrumentCatalog catalog = Catalog(
            new BybitHttpStub(_ => Response(
                Row("ABTCUSDT", "ABTC", "USDT"),
                Row("BTCUSDC", "BTC", "USDC"),
                Row("BTCUSDT", "BTC", "USDT"),
                Row("ETHBTC", "ETH", "BTC"))));

        IReadOnlyList<InstrumentInternal> btc = await catalog.Search("btc", limit: 10, CancellationToken.None);
        IReadOnlyList<InstrumentInternal> exact = await catalog.Search("btc/usdt", limit: 10, CancellationToken.None);
        IReadOnlyList<InstrumentInternal> limited = await catalog.Search("btc", limit: 1, CancellationToken.None);

        btc.Select(instrument => instrument.Symbol).ShouldBe(["BTCUSDC", "BTCUSDT", "ABTCUSDT", "ETHBTC"]);
        exact.Select(instrument => instrument.Symbol).ShouldBe(["BTCUSDT", "ABTCUSDT"]);
        limited.Select(instrument => instrument.Symbol).ShouldBe(["BTCUSDC"]);
    }

    [Test]
    public async Task TheListIsReadAgainOnlyAfterItsLifetime()
    {
        var handler = new BybitHttpStub(_ => Response(Row("BTCUSDT", "BTC", "USDT")));
        using BybitInstrumentCatalog catalog = Catalog(handler);

        await catalog.Search("BTC", limit: 10, CancellationToken.None);
        _clock.Now += TimeSpan.FromMinutes(9);
        await catalog.Search("ETH", limit: 10, CancellationToken.None);
        handler.Requests.Count.ShouldBe(1);
        _clock.Now += TimeSpan.FromMinutes(1);
        await catalog.Search("BTC", limit: 10, CancellationToken.None);

        handler.Requests.Count.ShouldBe(2);
    }

    [Test]
    public async Task AFailedRefreshKeepsThePreviousList()
    {
        var calls = 0;
        using BybitInstrumentCatalog catalog = Catalog(
            new BybitHttpStub(_ => ++calls == 1
                ? Response(Row("BTCUSDT", "BTC", "USDT"))
                : new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") }));

        await catalog.Search("BTC", limit: 10, CancellationToken.None);
        _clock.Now += TimeSpan.FromHours(1);
        IReadOnlyList<InstrumentInternal> stale = await catalog.Search("BTC", limit: 10, CancellationToken.None);

        stale.Select(instrument => instrument.Symbol).ShouldBe(["BTCUSDT"]);
        calls.ShouldBe(2);
    }

    [TestCase(403)]
    [TestCase(10001)]
    public async Task AFailureWithoutAnyList_IsUnavailable(int code)
    {
        using BybitInstrumentCatalog catalog = Catalog(
            new BybitHttpStub(_ => code < 10000
                ? new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { retCode = code, retMsg = "Rejected", result = new { } }))
                }));

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => catalog.Search("BTC", limit: 10, CancellationToken.None));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.ProviderUnavailable);
    }

    private BybitInstrumentCatalog Catalog(HttpMessageHandler handler)
    {
        return new BybitInstrumentCatalog(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.bybit.com/") },
            NullLoggerFactory.Instance,
            new BybitResilienceExecutor(TimeProvider.System, FastRetries, NullLogger<BybitResilienceExecutor>.Instance),
            FastRetries,
            _list);
    }

    private static object Row(string symbol, string baseAsset, string quoteAsset, string status = "Trading")
    {
        return new
        {
            symbol,
            baseCoin = baseAsset,
            quoteCoin = quoteAsset,
            status,
            lotSizeFilter = new { basePrecision = "0.000001", quotePrecision = "0.00000001", minOrderAmt = "5", minOrderQty = "0.000048" },
            priceFilter = new { tickSize = "0.01" }
        };
    }

    private static HttpResponseMessage Response(params object[] rows)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { retCode = 0, retMsg = "OK", result = new { category = "spot", list = rows } }))
        };
    }
}