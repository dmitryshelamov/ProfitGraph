using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Bybit.Instruments;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     Current Spot rules from Bybit's public instruments endpoint (Profit Fox Designer ADR 0021), against a local stub.
/// </summary>
public sealed class BybitInstrumentRulesSourceTests
{
    private static readonly InstrumentSymbol Symbol = new InstrumentSymbol("BTCUSDT");

    private static readonly BybitOptions FastRetries = new BybitOptions
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        AttemptTimeout = TimeSpan.FromSeconds(5),
        TotalTimeout = TimeSpan.FromSeconds(10)
    };

    [Test]
    public async Task PublicSpotEndpoint_CapturesCurrentRulesWithoutAuthentication()
    {
        var handler = new BybitHttpStub(_ => Response());
        using BybitInstrumentRulesSource source = Source(handler);

        InstrumentRulesDefinitionInternal definition = await source.Get(Symbol, CancellationToken.None);

        definition.ShouldSatisfyAllConditions(
            () => definition.Venue.ShouldBe("bybit"),
            () => definition.Source.ShouldBe("bybit"),
            () => definition.Market.ShouldBe("spot"),
            () => definition.Symbol.ShouldBe("BTCUSDT"),
            () => definition.BaseAsset.ShouldBe("BTC"),
            () => definition.QuoteAsset.ShouldBe("USDT"),
            () => definition.BasePrecisionIncrement.ShouldBe(0.000001m),
            () => definition.QuotePrecisionIncrement.ShouldBe(0.00000001m),
            () => definition.Rules.QuantityStep.ShouldBe(0.000001m),
            () => definition.Rules.PriceTick.ShouldBe(0.01m),
            () => definition.Rules.MinNotional.ShouldBe(5m),
            () => definition.Rules.MinQuantity.ShouldBe(0m),
            () => definition.Rules.BasePrecision.ShouldBe(6),
            () => definition.Rules.QuotePrecision.ShouldBe(8));
        Uri request = handler.Requests.Single();
        request.AbsolutePath.ShouldBe("/v5/market/instruments-info");
        request.Query.ShouldContain("category=spot");
        request.Query.ShouldContain("symbol=BTCUSDT");
    }

    [TestCase("1", arg2: 0)]
    [TestCase("0.1", arg2: 1)]
    [TestCase("0.00000100000000000000000000000", arg2: 6)]
    [TestCase("0.0000000000000000000000000001", arg2: 28)]
    public async Task ExactPowerOfTenPrecision_IsPreserved(string increment, int expected)
    {
        using BybitInstrumentRulesSource source = Source(new BybitHttpStub(_ => Response(baseIncrement: increment)));

        InstrumentRulesDefinitionInternal definition = await source.Get(Symbol, CancellationToken.None);

        definition.Rules.BasePrecision.ShouldBe(expected);
        definition.BasePrecisionIncrement.ShouldBe(decimal.Parse(increment, CultureInfo.InvariantCulture));
        definition.Rules.QuantityStep.ShouldBe(definition.BasePrecisionIncrement);
    }

    [TestCase("0")]
    [TestCase("0.03")]
    [TestCase("10")]
    [TestCase("0.00000000000000000000000000001")]
    [TestCase("0.00000100000000000000000000001")]
    [TestCase("79228162514264337593543950336")]
    [TestCase("NaN")]
    public async Task InvalidPrecision_IsRejectedWithoutSdkRounding(string increment)
    {
        var handler = new BybitHttpStub(_ => Response(baseIncrement: increment));
        using BybitInstrumentRulesSource source = Source(handler);

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => source.Get(Symbol, CancellationToken.None));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.InvalidProviderData);
        handler.Requests.Count.ShouldBe(1);
    }

    [TestCase("OTHER", "spot", "BTC", 1)]
    [TestCase("BTCUSDT", "linear", "BTC", 1)]
    [TestCase("BTCUSDT", "spot", "", 1)]
    [TestCase("BTCUSDT", "spot", "ETH", 1)]
    [TestCase("BTCUSDT", "spot", "BTC", 2)]
    public async Task InconsistentMetadata_IsRejected(string symbol, string category, string baseAsset, int count)
    {
        using BybitInstrumentRulesSource source = Source(new BybitHttpStub(_ => Response(symbol: symbol, category: category, baseAsset: baseAsset, count: count)));

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => source.Get(Symbol, CancellationToken.None));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.InvalidProviderData);
    }

    [TestCase("Trading", arg2: 0)]
    [TestCase("Closed", arg2: 1)]
    [TestCase("PreLaunch", arg2: 1)]
    public async Task UnknownOrNonTradingSymbol_IsAnInvalidRequest(string status, int count)
    {
        var handler = new BybitHttpStub(_ => Response(status: status, count: count));
        using BybitInstrumentRulesSource source = Source(handler);

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => source.Get(Symbol, CancellationToken.None));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.InvalidRequest);
        handler.Requests.Count.ShouldBe(1);
    }

    [TestCase("0", "5", "0.00000001")]
    [TestCase("0.01", "0", "0.00000001")]
    [TestCase("0.01000000000000000000000000001", "5", "0.00000001")]
    public async Task InvalidRules_AreRejected(string tick, string minimum, string quoteIncrement)
    {
        using BybitInstrumentRulesSource source = Source(new BybitHttpStub(_ => Response(tick: tick, minimum: minimum, quoteIncrement: quoteIncrement)));

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => source.Get(Symbol, CancellationToken.None));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task PriceTickAndMinimum_KeepTheirPrecisionIndependentOfTheQuoteAsset()
    {
        using BybitInstrumentRulesSource source = Source(new BybitHttpStub(_ => Response(tick: "0.001", minimum: "5.001", quoteIncrement: "0.01")));

        InstrumentRulesDefinitionInternal definition = await source.Get(Symbol, CancellationToken.None);

        definition.Rules.PriceTick.ShouldBe(0.001m);
        definition.Rules.MinNotional.ShouldBe(5.001m);
        definition.Rules.QuotePrecision.ShouldBe(2);
    }

    [Test]
    public async Task MissingRequiredRule_IsRejected()
    {
        using BybitInstrumentRulesSource source = Source(
            new BybitHttpStub(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"retCode":0,"result":{"category":"spot","list":[{"symbol":"BTCUSDT","lotSizeFilter":{}}]}}""")
            }));

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => source.Get(Symbol, CancellationToken.None));

        error.Code.ShouldBe(InstrumentRulesErrorCodes.InvalidProviderData);
    }

    [TestCase(503)]
    [TestCase(10006)]
    public async Task TransientFailure_IsRetried(int code)
    {
        var calls = 0;
        var handler = new BybitHttpStub(_ => ++calls == 1 ? Error(code) : Response());
        using BybitInstrumentRulesSource source = Source(handler);

        await source.Get(Symbol, CancellationToken.None);

        handler.Requests.Count.ShouldBe(2);
    }

    [TestCase(arg1: 10001, InstrumentRulesErrorCodes.InvalidRequest)]
    [TestCase(arg1: 403, InstrumentRulesErrorCodes.ProviderUnavailable)]
    public async Task PermanentFailure_IsNotRetried(int code, string expected)
    {
        var handler = new BybitHttpStub(_ => Error(code));
        using BybitInstrumentRulesSource source = Source(handler);

        var error = await Should.ThrowAsync<InstrumentRulesException>(() => source.Get(Symbol, CancellationToken.None));

        error.Code.ShouldBe(expected);
        handler.Requests.Count.ShouldBe(1);
    }

    private static BybitInstrumentRulesSource Source(HttpMessageHandler handler)
    {
        return new BybitInstrumentRulesSource(
            new HttpClient(new BybitInstrumentsResponseGuardHandler { InnerHandler = handler }) { BaseAddress = new Uri("https://api.bybit.com/") },
            NullLoggerFactory.Instance,
            new BybitResilienceExecutor(TimeProvider.System, FastRetries, NullLogger<BybitResilienceExecutor>.Instance),
            FastRetries);
    }

    private static HttpResponseMessage Error(int code)
    {
        return code < 10000
            ? new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{}") }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { retCode = code, retMsg = "Rejected", result = new { } }))
            };
    }

    private static HttpResponseMessage Response(
        string baseIncrement = "0.000001",
        string quoteIncrement = "0.00000001",
        string tick = "0.01",
        string minimum = "5",
        string symbol = "BTCUSDT",
        string category = "spot",
        string status = "Trading",
        string baseAsset = "BTC",
        int count = 1)
    {
        var row = new
        {
            symbol,
            baseCoin = baseAsset,
            quoteCoin = "USDT",
            status,
            lotSizeFilter = new { basePrecision = baseIncrement, quotePrecision = quoteIncrement, minOrderAmt = minimum, minOrderQty = "100" },
            priceFilter = new { tickSize = tick }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { retCode = 0, retMsg = "OK", result = new { category, list = Enumerable.Repeat(row, count).ToArray() } }))
        };
    }
}