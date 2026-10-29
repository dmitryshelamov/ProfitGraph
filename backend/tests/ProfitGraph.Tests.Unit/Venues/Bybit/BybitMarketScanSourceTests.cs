using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Venues.Bybit.MarketData;
using ProfitGraph.Venues.Bybit.Scanner;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

public sealed class BybitMarketScanSourceTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly InstrumentSymbol Symbol = new InstrumentSymbol("BTCUSDT");

    private static readonly BybitOptions Options = new BybitOptions
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        MaxRetryAttempts = 1,
        AttemptTimeout = TimeSpan.FromSeconds(5),
        TotalTimeout = TimeSpan.FromSeconds(10),
        ScannerRequestsPerSecond = 100
    };

    [Test]
    public async Task UniverseIsTheTradingPairsOfTheQuoteAsset()
    {
        var handler = new BybitHttpStub(_ => Json(
            new
            {
                category = "spot",
                list = new[]
                {
                    Instrument("ETHUSDT", "ETH", "USDT"),
                    Instrument("BTCUSDT", "BTC", "USDT"),
                    Instrument("ETHBTC", "ETH", "BTC"),
                    Instrument("OLDUSDT", "OLD", "USDT", "Closed"),
                    Instrument("BADUSDT", "BAD", "USDC")
                }
            }));
        using BybitMarketScanSource source = Source(handler);

        IReadOnlyList<ScanInstrumentInternal> universe = await source.Universe(CancellationToken.None);

        universe.ShouldBe([new ScanInstrumentInternal("BTCUSDT", ListedAt: null), new ScanInstrumentInternal("ETHUSDT", ListedAt: null)]);
    }

    [Test]
    public async Task CandlesPageBackwardsAndKeepTurnover()
    {
        DateTimeOffset to = From.AddMinutes(3);

        var handler = new BybitHttpStub(request => Query(request, "end") == Milliseconds(to.AddMilliseconds(-1))
            ? Klines([Kline(From.AddMinutes(2), "30"), Kline(From.AddMinutes(1), "20")])
            : Query(request, "end") == Milliseconds(From.AddMinutes(1).AddMilliseconds(-1))
                ? Klines([Kline(From, "10")])
                : Klines([]));
        using BybitMarketScanSource source = Source(handler);

        IReadOnlyList<ScanCandle> candles = await source.Candles(Symbol, Timeframe.Parse("1m"), From, to, CancellationToken.None);

        candles.Select(candle => candle.OpenTime).ShouldBe([From, From.AddMinutes(1), From.AddMinutes(2)]);
        candles.Select(candle => candle.Turnover).ShouldBe([10m, 20m, 30m]);
        Query(handler.Requests[0], "interval").ShouldBe("1");
        Query(handler.Requests[0], "limit").ShouldBe("1000");
    }

    [Test]
    public async Task CandlesOutsideTheRangeAreInvalidProviderData()
    {
        using BybitMarketScanSource source = Source(new BybitHttpStub(_ => Klines([Kline(From.AddMinutes(5), "10")])));

        var failure = await Should.ThrowAsync<MarketScanException>(() =>
            source.Candles(Symbol, Timeframe.Parse("1m"), From, From.AddMinutes(3), CancellationToken.None));

        failure.Code.ShouldBe(MarketScanErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task QuotesKeepPositiveBestPrices()
    {
        using BybitMarketScanSource source = Source(
            new BybitHttpStub(_ => Json(
                new
                {
                    category = "spot",
                    list = new[] { Ticker("BTCUSDT", "99", "101"), Ticker("NEWUSDT", "0", "0") }
                })));

        IReadOnlyList<ScanQuoteInternal> quotes = await source.Quotes(CancellationToken.None);

        quotes.ShouldBe([new ScanQuoteInternal("BTCUSDT", BestBid: 99m, BestAsk: 101m), new ScanQuoteInternal("NEWUSDT", BestBid: null, BestAsk: null)]);
    }

    [Test]
    public async Task AForbiddenAnswerPausesEveryLaterRequest()
    {
        var handler = new BybitHttpStub(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
        var budget = new BybitScanRequestBudget(Options, TimeProvider.System);
        using BybitMarketScanSource source = Source(handler, budget);

        var banned = await Should.ThrowAsync<MarketScanException>(() => source.Quotes(CancellationToken.None));
        var paused = await Should.ThrowAsync<MarketScanException>(() => source.Universe(CancellationToken.None));

        banned.Code.ShouldBe(MarketScanErrorCodes.RequestsPaused);
        paused.Code.ShouldBe(MarketScanErrorCodes.RequestsPaused);
        handler.Requests.Count.ShouldBe(1);
        budget.PausedUntil.ShouldNotBeNull();
    }

    [Test]
    public async Task StreamCarriesEveryPairOnOneConnection()
    {
        var transport = new FakePublicStreamTransport();
        using BybitMarketScanSource source = Source(new BybitHttpStub(_ => Klines([])), transport: transport);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<ScanCandleUpdateInternal> stream = source
            .Stream([Symbol, new InstrumentSymbol("ETHUSDT")], cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        ValueTask<bool> next = stream.MoveNextAsync();
        await WaitForConnection(transport);
        transport.Push(
            $$"""{"topic":"kline.1.ETHUSDT","data":[{"start":{{From.ToUnixTimeMilliseconds()}},"open":"1","high":"2","low":"1","close":"2","volume":"3","turnover":"4","confirm":true}]}""");

        (await next).ShouldBeTrue();
        stream.Current.Symbol.ShouldBe("ETHUSDT");
        stream.Current.Candle.Turnover.ShouldBe(4m);
        transport.LastTopics.ShouldBe(["kline.1.BTCUSDT", "kline.1.ETHUSDT"]);
        source.MaxSymbolsPerStream.ShouldBe(100);
    }

    [Test]
    public async Task StreamRefusesMoreThanOneConnectionCarries()
    {
        using BybitMarketScanSource source = Source(new BybitHttpStub(_ => Klines([])));
        InstrumentSymbol[] symbols = [.. Enumerable.Range(start: 0, count: 101).Select(index => new InstrumentSymbol($"T{index}USDT"))];

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await foreach (ScanCandleUpdateInternal _ in source.Stream(symbols, CancellationToken.None))
            {
            }
        });
    }

    private static BybitMarketScanSource Source(
        BybitHttpStub handler,
        BybitScanRequestBudget? budget = null,
        FakePublicStreamTransport? transport = null)
    {
        var baseAddress = new Uri("https://api.bybit.com/");

        return new BybitMarketScanSource(
            new HttpClient(new BybitResponseGuardHandler { InnerHandler = handler }) { BaseAddress = baseAddress },
            new HttpClient(handler) { BaseAddress = baseAddress },
            NullLoggerFactory.Instance,
            new BybitResilienceExecutor(TimeProvider.System, Options, NullLogger<BybitResilienceExecutor>.Instance),
            budget ?? new BybitScanRequestBudget(Options, TimeProvider.System),
            transport ?? new FakePublicStreamTransport(),
            Options);
    }

    private static async Task WaitForConnection(FakePublicStreamTransport transport)
    {
        for (var attempt = 0; attempt < 500 && transport.Connections == 0; attempt++)
        {
            await Task.Delay(millisecondsDelay: 10);
        }
    }

    private static object Instrument(string symbol, string baseAsset, string quoteAsset, string status = "Trading")
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

    private static object Ticker(string symbol, string bid, string ask)
    {
        return new
        {
            symbol,
            bid1Price = bid,
            bid1Size = "1",
            ask1Price = ask,
            ask1Size = "1",
            lastPrice = "100",
            prevPrice24h = "100",
            price24hPcnt = "0",
            highPrice24h = "100",
            lowPrice24h = "100",
            turnover24h = "0",
            volume24h = "0",
            usdIndexPrice = "100"
        };
    }

    private static string[] Kline(DateTimeOffset openTime, string turnover)
    {
        return [Milliseconds(openTime), "100", "110", "90", "105", "1", turnover];
    }

    private static HttpResponseMessage Klines(IEnumerable<string[]> rows)
    {
        return Json(new { category = "spot", symbol = "BTCUSDT", list = rows.ToArray() });
    }

    private static HttpResponseMessage Json(object result)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { retCode = 0, retMsg = "OK", result, time = From.ToUnixTimeMilliseconds() }))
        };
    }

    private static string Milliseconds(DateTimeOffset time)
    {
        return time.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private static string Query(Uri request, string key)
    {
        return Uri.UnescapeDataString(request.Query.TrimStart('?').Split('&').Single(part => part.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..]);
    }
}