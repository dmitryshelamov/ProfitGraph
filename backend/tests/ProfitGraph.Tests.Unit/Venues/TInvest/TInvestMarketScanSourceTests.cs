using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;
using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Scanner;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using Candle = Tinkoff.InvestApi.V1.Candle;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The market scanner's T-Invest source (ADR 0062) on the in-process Invest API mock: the scanner's credential, the universe of
///     main-board rouble shares, candles in shares with a typical-price turnover and without weekend sessions, the request pause
///     after a rate limit, and closed minutes over the stream with its pings reported as alive. No real token or request is
///     involved.
/// </summary>
public sealed class TInvestMarketScanSourceTests
{
    // Thursday, 10:00 in Moscow.
    private static readonly DateTimeOffset Thursday = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 7, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Saturday = Thursday.AddDays(2);
    private static readonly Guid CredentialId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Timeframe Minute = Timeframe.Parse("1m");

    private readonly List<MockTInvestDuplexCall<MarketDataRequest, MarketDataResponse>> _streams = [];
    private MockTInvestApi _api = null!;
    private MockTInvestMarket _market = null!;
    private IExchangeCredentialRepository _credentials = null!;
    private ServiceProvider _services = null!;
    private TInvestOptions _options = null!;
    private Share _sber = null!;

    [SetUp]
    public void CreateMock()
    {
        _streams.Clear();
        _api = new MockTInvestApi();
        _market = new MockTInvestMarket(_api);
        _sber = _market.AddShare("SBER", lot: 10, increment: 0.01m, Thursday.AddYears(-5));
        _api.OnDuplex<MarketDataRequest, MarketDataResponse>(
            MarketDataStreamService.Descriptor,
            "MarketDataStream",
            stream =>
            {
                lock (_streams)
                {
                    _streams.Add(stream);
                }
            });
        _credentials = Substitute.For<IExchangeCredentialRepository>();
        _credentials.Get(new ExchangeCredentialId(CredentialId), Arg.Any<CancellationToken>()).Returns(Credential("tinvest"));
        var sender = Substitute.For<ISender>();
        sender.Send(new ReadExchangeCredentialSecretsQuery(OwnerId, CredentialId), Arg.Any<CancellationToken>())
            .Returns(
                new ExchangeCredentialSecretsInternal(
                    CredentialId,
                    "tinvest",
                    new Dictionary<string, string> { [TInvestCredentialFields.Token] = "t.invented-scanner-token" }));
        _services = new ServiceCollection().AddSingleton(_credentials).AddSingleton(sender).BuildServiceProvider();
        _options = Configured(CredentialId);
    }

    [TearDown]
    public async Task DisposeServices()
    {
        await _services.DisposeAsync();
    }

    [Test]
    public async Task TheUniverseIsTheMainBoardRoubleSharesTheApiBuysAndSells()
    {
        _sber.IpoDate = Timestamp.FromDateTimeOffset(new DateTimeOffset(year: 2007, month: 7, day: 19, hour: 0, minute: 0, second: 0, TimeSpan.Zero));
        _market.AddShare("GAZP", lot: 10, increment: 0.01m, Thursday.AddYears(-5));
        _market.AddShare("USDS", lot: 1, increment: 0.01m, Thursday, "usd");
        _market.AddShare("NOAPI", lot: 1, increment: 0.01m, Thursday, apiTrading: false);
        _market.AddShare("QUAL", lot: 1, increment: 0.01m, Thursday).ForQualInvestorFlag = true;
        _market.AddShare("NOBUY", lot: 1, increment: 0.01m, Thursday).BuyAvailableFlag = false;
        _market.AddShare("SPB", lot: 1, increment: 0.01m, Thursday).ClassCode = "SPBXM";

        IReadOnlyList<ScanInstrumentInternal> universe = await Source().Universe(CancellationToken.None);

        universe.ShouldBe(
        [
            new ScanInstrumentInternal("TQBR:GAZP", ListedAt: null),
            new ScanInstrumentInternal("TQBR:SBER", new DateTimeOffset(year: 2007, month: 7, day: 19, hour: 0, minute: 0, second: 0, TimeSpan.Zero))
        ]);
        _api.Calls.Single().Headers.GetValue("authorization").ShouldBe("Bearer t.invented-scanner-token");
    }

    [Test]
    public async Task SharesOnlyQualifiedInvestorsBuyAreScannedWhenTheOwnerSaysSo()
    {
        _market.AddShare("QUAL", lot: 1, increment: 0.01m, Thursday).ForQualInvestorFlag = true;
        _options = Configured(CredentialId, qualified: true);

        IReadOnlyList<ScanInstrumentInternal> universe = await Source().Universe(CancellationToken.None);

        universe.Select(instrument => instrument.Symbol).ShouldBe(["TQBR:QUAL", "TQBR:SBER"]);
    }

    [Test]
    public async Task CandlesAreInSharesWithATypicalPriceTurnover_AndWeekendSessionsAreLeftOut()
    {
        _market.AddCandle("SBER", Thursday, close: 300m, lots: 4);
        _market.AddCandle("SBER", Thursday.AddMinutes(1), close: 301m, lots: 0);
        _market.AddCandle("SBER", Saturday, close: 310m, lots: 2);

        IReadOnlyList<ScanCandle> candles = await Source()
            .Candles(new InstrumentSymbol("TQBR:SBER"), Minute, Thursday, Saturday.AddMinutes(1), CancellationToken.None);

        candles.ShouldBe(
        [
            new ScanCandle(Thursday, Open: 300m, High: 301m, Low: 299m, Close: 300m, Volume: 40m, Turnover: 12_000m),
            new ScanCandle(Thursday.AddMinutes(1), Open: 301m, High: 302m, Low: 300m, Close: 301m, Volume: 0m, Turnover: 0m)
        ]);

        // One request per day of minutes, each for the exchange's candles only.
        _market.CandleRequests.Count.ShouldBe(3);
        _market.CandleRequests.ShouldAllBe(request => request.CandleSourceType == GetCandlesRequest.Types.CandleSource.Exchange && !request.HasLimit);
    }

    [Test]
    public async Task ACandleThatIsNotTheExchangesIsInvalidProviderData()
    {
        _market.AddCandle("SBER", Thursday, close: 300m, source: CandleSource.DealerWeekend);

        var failure = await Should.ThrowAsync<MarketScanException>(() =>
            Source().Candles(new InstrumentSymbol("TQBR:SBER"), Minute, Thursday, Thursday.AddMinutes(5), CancellationToken.None));

        failure.Code.ShouldBe(MarketScanErrorCodes.InvalidProviderData);
    }

    [Test]
    public async Task ARateLimitPausesTheScannersRequests()
    {
        _api.On<GetCandlesRequest, GetCandlesResponse>(
            MarketDataService.Descriptor,
            "GetCandles",
            _ => throw MockTInvestApi.Refusal(StatusCode.ResourceExhausted, "80002"));
        TInvestMarketScanSource source = Source();

        var limited = await Should.ThrowAsync<MarketScanException>(() =>
            source.Candles(new InstrumentSymbol("TQBR:SBER"), Minute, Thursday, Thursday.AddMinutes(5), CancellationToken.None));
        int calls = _api.Calls.Count;
        var paused = await Should.ThrowAsync<MarketScanException>(() =>
            source.Candles(new InstrumentSymbol("TQBR:SBER"), Minute, Thursday, Thursday.AddMinutes(5), CancellationToken.None));

        limited.Code.ShouldBe(MarketScanErrorCodes.RequestsPaused);
        paused.Code.ShouldBe(MarketScanErrorCodes.RequestsPaused);
        _api.Calls.Count.ShouldBe(calls);
    }

    [Test]
    public async Task WithoutTheScannersCredentialNothingIsSent()
    {
        _credentials.Get(new ExchangeCredentialId(CredentialId), Arg.Any<CancellationToken>()).Returns(Credential("bybit"));

        var failure = await Should.ThrowAsync<MarketScanException>(() => Source().Universe(CancellationToken.None));

        failure.Code.ShouldBe(MarketScanErrorCodes.ProviderUnavailable);
        _api.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task TheStreamSubscribesClosedExchangeMinutes_ReportsItsPingsAsAlive_AndDeliversClosedCandlesInShares()
    {
        Share gazp = _market.AddShare("GAZP", lot: 1, increment: 0.01m, Thursday.AddYears(-5));
        TInvestMarketScanSource source = Source();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ScanCandleUpdateInternal> updates = source
            .Stream([new InstrumentSymbol("TQBR:SBER"), new InstrumentSymbol("TQBR:GAZP")], cancel.Token)
            .GetAsyncEnumerator(cancel.Token);

        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 2);
        MockTInvestDuplexCall<MarketDataRequest, MarketDataResponse> stream = Stream(0)!;
        stream.Requests[0].PingSettings.PingDelayMs.ShouldBe(30_000);
        SubscribeCandlesRequest subscribe = stream.Requests[1].SubscribeCandlesRequest;
        subscribe.ShouldSatisfyAllConditions(
            request => request.SubscriptionAction.ShouldBe(SubscriptionAction.Subscribe),
            request => request.WaitingClose.ShouldBeTrue(),
            request => request.CandleSourceType.ShouldBe(GetCandlesRequest.Types.CandleSource.Exchange),
            request => request.Instruments.Select(instrument => instrument.InstrumentId).ShouldBe([_sber.Uid, gazp.Uid], ignoreOrder: true),
            request => request.Instruments.ShouldAllBe(instrument => instrument.Interval == SubscriptionInterval.OneMinute));
        stream.Headers.GetValue("authorization").ShouldBe("Bearer t.invented-scanner-token");

        stream.Push(Subscribed(SubscriptionStatus.Success));
        (await next).ShouldBeTrue();
        updates.Current.IsAlive.ShouldBeTrue();

        stream.Push(new MarketDataResponse { Ping = new Ping { Time = Timestamp.FromDateTimeOffset(Thursday) } });
        (await updates.MoveNextAsync()).ShouldBeTrue();
        updates.Current.IsAlive.ShouldBeTrue();

        stream.Push(Update(_sber.Uid, Saturday, close: 310m, lots: 1));
        stream.Push(Update(Guid.NewGuid().ToString("D"), Thursday, close: 1m, lots: 1));
        stream.Push(Update(_sber.Uid, Thursday.AddSeconds(30), close: 300m, lots: 1));
        stream.Push(Update(_sber.Uid, Thursday, close: 300m, lots: 4));
        (await updates.MoveNextAsync()).ShouldBeTrue();
        updates.Current.ShouldBe(
            new ScanCandleUpdateInternal(
                "TQBR:SBER",
                new ScanCandle(Thursday, Open: 300m, High: 301m, Low: 299m, Close: 300m, Volume: 40m, Turnover: 12_000m),
                Closed: true));
    }

    [Test]
    public async Task ARefusedSubscriptionEndsTheStream()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<ScanCandleUpdateInternal> updates = Source()
            .Stream([new InstrumentSymbol("TQBR:SBER")], cancel.Token)
            .GetAsyncEnumerator(cancel.Token);

        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 2);
        Stream(0)!.Push(Subscribed(SubscriptionStatus.LimitIsExceeded));

        var failure = await Should.ThrowAsync<MarketScanException>(() => next);
        failure.Code.ShouldBe(MarketScanErrorCodes.ProviderUnavailable);
    }

    [Test]
    public async Task AStreamCarriesAtMostTheConfiguredShares()
    {
        _options = Configured(CredentialId, sharesPerStream: 1);
        TInvestMarketScanSource source = Source();

        source.MaxSymbolsPerStream.ShouldBe(1);
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await foreach (ScanCandleUpdateInternal _ in source.Stream([new InstrumentSymbol("TQBR:SBER"), new InstrumentSymbol("TQBR:GAZP")], CancellationToken.None))
            {
            }
        });
    }

    [Test]
    public async Task TheVenueQuotesInRoublesWithItsOwnPresetsAndNoSpreads()
    {
        TInvestMarketScanSource source = Source();

        source.QuoteAsset.ShouldBe("RUB");
        source.BuiltInPresets.Select(preset => preset.Name).ShouldBe(["Импульс", "Под лесенку"]);
        source.BuiltInPresets.ShouldAllBe(preset => preset.Venue == "tinvest" && preset.BuiltIn && preset.HideStale);
        source.BuiltInPresets.SelectMany(preset => preset.Conditions).ShouldNotContain(condition => condition.Metric == "Spread");
        (await source.Quotes(CancellationToken.None)).ShouldBeEmpty();
    }

    private static TInvestOptions Configured(Guid? credentialId, bool qualified = false, int sharesPerStream = 50)
    {
        return new TInvestOptions
        {
            Enabled = true,
            Scanner = new TInvestScannerOptions
            {
                CredentialId = credentialId,
                RequestsPerMinute = 6000,
                SubscriptionsPerMinute = 6000,
                SharesPerStream = sharesPerStream,
                QualifiedInvestor = qualified
            }
        };
    }

    private static ExchangeCredential Credential(string venue)
    {
        return ExchangeCredential.Restore(
            new ExchangeCredentialId(CredentialId),
            new UserId(OwnerId),
            new VenueId(venue),
            new ExchangeCredentialLabel("scanner"),
            new ApiKeyLast4("0001"),
            new Dictionary<string, EncryptedSecret>
            {
                [TInvestCredentialFields.Token] = new EncryptedSecret("test", new byte[EncryptedSecret.NonceLength], [1], new byte[EncryptedSecret.TagLength])
            },
            Thursday);
    }

    private static MarketDataResponse Subscribed(SubscriptionStatus status)
    {
        var response = new SubscribeCandlesResponse();
        response.CandlesSubscriptions.Add(new CandleSubscription { Interval = SubscriptionInterval.OneMinute, SubscriptionStatus = status });

        return new MarketDataResponse { SubscribeCandlesResponse = response };
    }

    private static MarketDataResponse Update(string uid, DateTimeOffset openTime, decimal close, long lots)
    {
        return new MarketDataResponse
        {
            Candle = new Candle
            {
                InstrumentUid = uid,
                Interval = SubscriptionInterval.OneMinute,
                Time = Timestamp.FromDateTimeOffset(openTime),
                Open = close.ToQuotation(),
                High = (close + 1m).ToQuotation(),
                Low = (close - 1m).ToQuotation(),
                Close = close.ToQuotation(),
                Volume = lots,
                CandleSourceType = CandleSource.Exchange
            }
        };
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);

        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The expected state was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private TInvestMarketScanSource Source()
    {
        IOptions<TInvestOptions> options = Options.Create(_options);
        var transport = new TInvestTransport(
            new MockTInvestInvokers(_api),
            new TInvestRequestLimiter(new TInvestRequestLimits { MarketData = 1000, Instruments = 1000 }, TimeProvider.System),
            options);

        return new TInvestMarketScanSource(
            transport,
            new TInvestShares(transport, options, TimeProvider.System),
            new TInvestScanMarketData(_services.GetRequiredService<IServiceScopeFactory>(), options, TimeProvider.System),
            new TInvestScanBudget(options, TimeProvider.System),
            options);
    }

    private MockTInvestDuplexCall<MarketDataRequest, MarketDataResponse>? Stream(int index)
    {
        lock (_streams)
        {
            return _streams.Count > index ? _streams[index] : null;
        }
    }
}