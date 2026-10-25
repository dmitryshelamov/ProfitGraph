using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

public sealed class BybitHistoricalCandleSourceTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly InstrumentSymbol Symbol = new InstrumentSymbol("BTCUSDT");
    private static readonly Timeframe Hour = Timeframe.Parse("1h");

    private static readonly BybitOptions FastRetries = new BybitOptions
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(10),
        AttemptTimeout = TimeSpan.FromMilliseconds(500),
        TotalTimeout = TimeSpan.FromSeconds(5)
    };

    [Test]
    public async Task Pagination_UsesExclusiveEndAndAscendingOutput()
    {
        var handler = new BybitHttpStub(request => long.Parse(Query(request, "end"), CultureInfo.InvariantCulture) == From.AddHours(3).ToUnixTimeMilliseconds() - 1
            ? Response([Row(From.AddHours(2)), Row(From.AddHours(1)), Row(From.AddHours(1))])
            : Response([Row(From)]));
        using BybitHistoricalCandleSource source = Source(handler);

        IReadOnlyList<Candle> candles = (await source.Get(Symbol, Hour, From, From.AddHours(3), CancellationToken.None)).Candles;

        candles.Select(candle => candle.OpenTime).ShouldBe([From, From.AddHours(1), From.AddHours(2)]);
        handler.Requests.Count.ShouldBe(2);
        Query(handler.Requests[0], "category").ShouldBe("spot");
        Query(handler.Requests[0], "start").ShouldBe(From.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        Query(handler.Requests[1], "end").ShouldBe((From.AddHours(1).ToUnixTimeMilliseconds() - 1).ToString(CultureInfo.InvariantCulture));
    }

    [TestCase("1m", "1")]
    [TestCase("3m", "3")]
    [TestCase("5m", "5")]
    [TestCase("15m", "15")]
    [TestCase("30m", "30")]
    [TestCase("1h", "60")]
    [TestCase("2h", "120")]
    [TestCase("4h", "240")]
    [TestCase("6h", "360")]
    [TestCase("12h", "720")]
    [TestCase("1d", "D")]
    public async Task SupportedTimeframes_MapToSpotIntervals(string timeframe, string expected)
    {
        var handler = new BybitHttpStub(_ => Response([]));
        using BybitHistoricalCandleSource source = Source(handler);

        await source.Get(Symbol, Timeframe.Parse(timeframe), From, From.AddDays(1), CancellationToken.None);

        Query(handler.Requests.Single(), "interval").ShouldBe(expected);
    }

    [TestCase("100.00000000000000000000000000001")]
    [TestCase("79228162514264337593543950336")]
    [TestCase("Infinity")]
    [TestCase("1e3")]
    public async Task UnrepresentableDecimals_AreRejectedBeforeSdkRounding(string open)
    {
        using BybitHistoricalCandleSource source = Source(new BybitHttpStub(_ => Response([Row(From, open)])));

        var error = await Should.ThrowAsync<HistoricalDataException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None));

        error.Code.ShouldBe(HistoricalDataErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task ExactValuesSurviveTheSdk()
    {
        using BybitHistoricalCandleSource source = Source(new BybitHttpStub(_ => Response([Row(From, "100.1234567890123456789")])));

        IReadOnlyList<Candle> candles = (await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None)).Candles;

        candles.Single().Open.ShouldBe(100.1234567890123456789m);
        candles.Single().Volume.ShouldBe(1m);
    }

    [TestCase("OTHER", "spot")]
    [TestCase("BTCUSDT", "linear")]
    public async Task DifferentMarketOrSymbol_IsRejected(string symbol, string category)
    {
        using BybitHistoricalCandleSource source = Source(new BybitHttpStub(_ => Response([Row(From)], symbol, category)));

        var error = await Should.ThrowAsync<HistoricalDataException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None));

        error.Code.ShouldBe(HistoricalDataErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task OutOfRangeOrInconsistentCandles_AreRejected()
    {
        using BybitHistoricalCandleSource outOfRange = Source(new BybitHttpStub(_ => Response([Row(From.AddHours(5))])));
        using BybitHistoricalCandleSource inconsistent = Source(new BybitHttpStub(_ => Response([Row(From, "200")])));

        (await Should.ThrowAsync<HistoricalDataException>(() => outOfRange.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None))).Code
            .ShouldBe(HistoricalDataErrorCodes.InvalidProviderData);
        (await Should.ThrowAsync<HistoricalDataException>(() => inconsistent.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None))).Code
            .ShouldBe(HistoricalDataErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task EarliestDiscovery_UsesExistencePrefix_DespiteInternalGap()
    {
        var handler = new BybitHttpStub(request =>
        {
            long start = long.Parse(Query(request, "start"), CultureInfo.InvariantCulture);
            long end = long.Parse(Query(request, "end"), CultureInfo.InvariantCulture);
            DateTimeOffset[] times = [From, From.AddHours(2)];

            return Response(
                times.Where(time => time.ToUnixTimeMilliseconds() >= start && time.ToUnixTimeMilliseconds() <= end).OrderDescending().Take(1).Select(time => Row(time)));
        });
        using BybitHistoricalCandleSource source = Source(handler);

        (await source.FindFirst(Symbol, Hour, From.AddHours(4), CancellationToken.None)).ShouldBe(From);
        handler.Requests.Count.ShouldBeLessThan(30);
    }

    [Test]
    public async Task EarliestDiscovery_ReportsNoHistory()
    {
        using BybitHistoricalCandleSource source = Source(new BybitHttpStub(_ => Response([])));

        (await source.FindFirst(Symbol, Hour, From.AddHours(4), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Cancellation_StopsWithoutAnotherRequest()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new BybitHttpStub(_ => Response([]));
        using BybitHistoricalCandleSource source = Source(handler);

        await Should.ThrowAsync<OperationCanceledException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), cancellation.Token));

        handler.Requests.ShouldBeEmpty();
    }

    [TestCase(429)]
    [TestCase(500)]
    [TestCase(503)]
    [TestCase(10006)]
    [TestCase(10000)]
    [TestCase(10016)]
    [TestCase(170007)]
    public async Task TransientFailure_IsRetried(int failure)
    {
        var calls = 0;
        var handler = new BybitHttpStub(_ => ++calls == 1
            ? failure >= 10000 ? ErrorResponse(failure) : new HttpResponseMessage((HttpStatusCode)failure) { Content = new StringContent("{}") }
            : Response([]));
        using BybitHistoricalCandleSource source = Source(handler);

        await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None);

        calls.ShouldBe(2);
    }

    [TestCase(10001)]
    [TestCase(10029)]
    [TestCase(170121)]
    [TestCase(170130)]
    public async Task InvalidSymbolOrParameters_AreNotRetried(int code)
    {
        var handler = new BybitHttpStub(_ => ErrorResponse(code));
        using BybitHistoricalCandleSource source = Source(handler);

        var error = await Should.ThrowAsync<HistoricalDataException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None));

        error.Code.ShouldBe(HistoricalDataErrorCodes.InvalidRequest);
        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Forbidden_IsNotRetried()
    {
        var handler = new BybitHttpStub(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
        using BybitHistoricalCandleSource source = Source(handler);

        await Should.ThrowAsync<HistoricalDataException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None));

        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task ExhaustedRateLimit_StopsAfterThreeRetries()
    {
        var handler = new BybitHttpStub(_ => ErrorResponse(code: 10006, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        using BybitHistoricalCandleSource source = Source(handler);

        var error = await Should.ThrowAsync<HistoricalDataException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None));

        error.Code.ShouldBe(HistoricalDataErrorCodes.ProviderUnavailable);
        handler.Requests.Count.ShouldBe(4);
    }

    [Test]
    public async Task RateLimit_RetryWaitsUntilBybitResetTimestamp()
    {
        var calls = 0;
        long reset = DateTimeOffset.UtcNow.AddMilliseconds(700).ToUnixTimeMilliseconds();
        var handler = new BybitHttpStub(_ =>
        {
            if (++calls == 1)
            {
                return ErrorResponse(code: 10006, reset);
            }

            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ShouldBeGreaterThanOrEqualTo(reset);

            return Response([]);
        });
        using BybitHistoricalCandleSource source = Source(handler);

        await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None);

        calls.ShouldBe(2);
    }

    [Test]
    public async Task Cancellation_InterruptsRateLimitWait()
    {
        using var cancellation = new CancellationTokenSource();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BybitHttpStub(_ =>
        {
            received.TrySetResult();

            return ErrorResponse(code: 10006, DateTimeOffset.UtcNow.AddSeconds(3).ToUnixTimeMilliseconds());
        });
        using BybitHistoricalCandleSource source = Source(handler);
        Task<HistoricalCandlesInternal> download = source.Get(Symbol, Hour, From, From.AddHours(1), cancellation.Token);
        await received.Task;
        await Task.Delay(millisecondsDelay: 50, CancellationToken.None);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => download);
        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task RateLimit_ResetBeyondTheBudget_FailsWithoutWaiting()
    {
        var handler = new BybitHttpStub(_ => ErrorResponse(code: 10006, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()));
        using BybitHistoricalCandleSource source = Source(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var error = await Should.ThrowAsync<HistoricalDataException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), cancellation.Token));

        error.Code.ShouldBe(HistoricalDataErrorCodes.ProviderUnavailable);
        handler.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task RateLimit_ResetBeyondTheRemainingBudget_FailsWithoutWaiting()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        TimeSpan elapsed = TimeSpan.Zero;
        var clock = Substitute.ForPartsOf<TimeProvider>();
        clock.TimestampFrequency.Returns(TimeSpan.TicksPerSecond);
        clock.GetTimestamp().Returns(_ => elapsed.Ticks);
        clock.GetUtcNow().Returns(_ => started + elapsed);
        var handler = new BybitHttpStub(_ =>
        {
            elapsed = TimeSpan.FromSeconds(3);

            return ErrorResponse(code: 10006, started.AddSeconds(6).ToUnixTimeMilliseconds());
        });
        using BybitHistoricalCandleSource source = Source(handler, clock);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var error = await Should.ThrowAsync<HistoricalDataException>(() => source.Get(Symbol, Hour, From, From.AddHours(1), cancellation.Token));

        error.Code.ShouldBe(HistoricalDataErrorCodes.ProviderUnavailable);
        handler.Requests.Count.ShouldBe(1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RetryAfterHeader_DelaysTheRetry(bool asDate)
    {
        DateTimeOffset retryAt = DateTimeOffset.UtcNow.AddSeconds(2);
        var calls = 0;
        DateTimeOffset? retriedAt = null;
        var handler = new BybitHttpStub(_ =>
        {
            if (++calls == 1)
            {
                // A 503 rather than a 429: the SDK keeps 429 limits for the whole process, which would leak into other tests.
                var limited = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
                limited.Headers.Add("Retry-After", asDate ? retryAt.ToString("r", CultureInfo.InvariantCulture) : "1");

                return limited;
            }

            retriedAt = DateTimeOffset.UtcNow;

            return Response([]);
        });
        using BybitHistoricalCandleSource source = Source(handler);
        DateTimeOffset started = DateTimeOffset.UtcNow;

        await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None);

        calls.ShouldBe(2);
        (retriedAt!.Value - started).ShouldBeGreaterThanOrEqualTo(asDate ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(950));
    }

    [Test]
    public async Task InvalidResetTimestamp_FallsBackToBoundedRetry()
    {
        var calls = 0;
        var handler = new BybitHttpStub(_ =>
        {
            if (++calls != 1)
            {
                return Response([]);
            }

            HttpResponseMessage response = ErrorResponse(10006);
            response.Headers.Add("X-Bapi-Limit-Reset-Timestamp", "invalid");

            return response;
        });
        using BybitHistoricalCandleSource source = Source(handler);

        await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None);

        calls.ShouldBe(2);
    }

    [Test]
    public async Task AttemptTimeout_CancelsAHangingCall_ThenRetries()
    {
        var handler = new BybitHangingHttpStub(() => Response([]));
        using BybitHistoricalCandleSource source = Source(handler);

        await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None);

        handler.Requests.ShouldBe(2);
    }

    [Test]
    public async Task TransportTimeout_IsRetried()
    {
        var calls = 0;
        var handler = new BybitHttpStub(_ => ++calls == 1 ? throw new TaskCanceledException("Simulated transport timeout.") : Response([]));
        using BybitHistoricalCandleSource source = Source(handler);

        await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None);

        calls.ShouldBe(2);
    }

    [Test]
    public async Task RegisteredSource_UsesTheConfiguredOriginAndTheSystemClock()
    {
        var handler = new BybitHttpStub(_ => Response([]));
        await using ServiceProvider services = Services(handler, "https://bybit-history.example/", new FrozenClock());
        var source = services.GetRequiredService<IHistoricalCandleSource>();

        await source.Get(Symbol, Hour, From, From.AddHours(1), CancellationToken.None);

        source.Venue.ShouldBe(new VenueId("bybit"));
        handler.Requests.Single().Host.ShouldBe("bybit-history.example");
        handler.Requests.Single().AbsolutePath.ShouldBe("/v5/market/kline");
    }

    [TestCase("")]
    [TestCase("not-a-uri")]
    [TestCase("ftp://api.bybit.com/")]
    [TestCase("https://user:password@api.bybit.com/")]
    [TestCase("https://api.bybit.com/path")]
    [TestCase("https://api.bybit.com/?secret=value")]
    public void InvalidOrigin_IsRejected(string address)
    {
        var handler = new BybitHttpStub(_ => Response([]));
        using ServiceProvider services = Services(handler, address);

        Should.Throw<OptionsValidationException>(services.GetRequiredService<IHistoricalCandleSource>);
        handler.Requests.ShouldBeEmpty();
    }

    private static BybitHistoricalCandleSource Source(HttpMessageHandler handler, TimeProvider? clock = null)
    {
        return new BybitHistoricalCandleSource(
            new HttpClient(new BybitResponseGuardHandler { InnerHandler = handler }) { BaseAddress = new Uri("https://api.bybit.com/") },
            NullLoggerFactory.Instance,
            new BybitResilienceExecutor(clock ?? TimeProvider.System, FastRetries, NullLogger<BybitResilienceExecutor>.Instance),
            FastRetries);
    }

    private static ServiceProvider Services(BybitHttpStub handler, string baseAddress, TimeProvider? applicationClock = null)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Venues:Bybit:BaseAddress"] = baseAddress })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        if (applicationClock is not null)
        {
            services.AddSingleton(applicationClock);
        }

        services.AddBybitVenue(configuration);
        services.AddHttpClient<IHistoricalCandleSource, BybitHistoricalCandleSource>().ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static string[] Row(DateTimeOffset time, string open = "100")
    {
        return [time.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), open, "110", "90", "105", "1.00000000000000000000000000000", "100"];
    }

    private static HttpResponseMessage Response(IEnumerable<string[]> rows, string symbol = "BTCUSDT", string category = "spot")
    {
        var body = new { retCode = 0, retMsg = "OK", result = new { category, symbol, list = rows.ToArray() }, time = From.ToUnixTimeMilliseconds() };

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }

    private static HttpResponseMessage ErrorResponse(int code, long? resetTimestamp = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { retCode = code, retMsg = "Rejected request", result = new { } }))
        };

        if (resetTimestamp is not null)
        {
            response.Headers.Add("X-Bapi-Limit-Reset-Timestamp", resetTimestamp.Value.ToString(CultureInfo.InvariantCulture));
        }

        return response;
    }

    private static string Query(Uri request, string key)
    {
        return Uri.UnescapeDataString(request.Query.TrimStart('?').Split('&').Single(part => part.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..]);
    }
}