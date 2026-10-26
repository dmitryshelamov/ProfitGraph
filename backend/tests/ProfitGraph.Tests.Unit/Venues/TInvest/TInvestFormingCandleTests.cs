using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.LiveData;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using Candle = Tinkoff.InvestApi.V1.Candle;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     A share's forming candle over the market data stream (ADR 0026): one stream per token shared by every subscription, the
///     token only as call metadata, lots turned into shares, bad updates dropped and a lost stream ending its subscriptions.
///     The Invest API is the in-process mock; no real token or stream is involved.
/// </summary>
public sealed class TInvestFormingCandleTests
{
    private static readonly DateTimeOffset Open = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 7, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly InstrumentSymbol Sber = new InstrumentSymbol("TQBR:SBER");
    private static readonly InstrumentSymbol Gazp = new InstrumentSymbol("TQBR:GAZP");
    private static readonly Timeframe Minute = Timeframe.Parse("1m");
    private static readonly Timeframe FiveMinutes = Timeframe.Parse("5m");

    private static readonly ExchangeCredentialSecretsInternal Credential = new ExchangeCredentialSecretsInternal(
        Guid.NewGuid(),
        "tinvest",
        new Dictionary<string, string> { [TInvestCredentialFields.Token] = "t.invented-token", [TInvestCredentialFields.AccountId] = "2000000001" });

    private readonly List<MockTInvestDuplexCall<MarketDataRequest, MarketDataResponse>> _streams = [];
    private MockTInvestApi _api = null!;
    private MockTInvestMarket _market = null!;
    private FakeTimeProvider _clock = null!;
    private TInvestFormingCandleSource _source = null!;
    private Share _sber = null!;
    private Share _gazp = null!;

    [SetUp]
    public void CreateMock()
    {
        _streams.Clear();
        _api = new MockTInvestApi();
        _market = new MockTInvestMarket(_api);
        _clock = new FakeTimeProvider(Open.AddSeconds(30));
        _sber = _market.AddShare("SBER", lot: 10, increment: 0.01m, Open.AddDays(-30));
        _gazp = _market.AddShare("GAZP", lot: 1, increment: 0.01m, Open.AddDays(-30));
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
        var transport = new TInvestTransport(
            new MockTInvestInvokers(_api),
            new TInvestRequestLimiter(new TInvestRequestLimits { MarketData = 1000, Instruments = 1000 }, _clock),
            Options.Create(new TInvestOptions()));
        _source = new TInvestFormingCandleSource(new TInvestShares(transport, _clock), new TInvestCandleStreams(transport, NullLoggerFactory.Instance), _clock);
    }

    [TestCase("1m", arg2: true)]
    [TestCase("3m", arg2: true)]
    [TestCase("4h", arg2: true)]
    [TestCase("6h", arg2: false)]
    [TestCase("1d", arg2: false)]
    public void OnlyTheTimeframesTheBrokerStreamsAreSupported(string timeframe, bool supported)
    {
        _source.Supports(Timeframe.Parse(timeframe)).ShouldBe(supported);
    }

    [Test]
    public async Task ASubscriptionAsksTheStreamForTheShareAndDeliversItsCandlesInShares()
    {
        using IDisposable access = MarketDataAccess.Use(Credential);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<FormingCandleInternal> updates = _source.Subscribe(Sber, Minute, cancel.Token).GetAsyncEnumerator(cancel.Token);

        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 1);
        Stream(0)!.Push(Update(_sber, Open, close: 250.5m, lots: 4));

        (await next).ShouldBeTrue();
        SubscribeCandlesRequest request = Stream(0)!.Requests.Single().SubscribeCandlesRequest;
        request.SubscriptionAction.ShouldBe(SubscriptionAction.Subscribe);
        request.Instruments.Single()
            .ShouldSatisfyAllConditions(
                instrument => instrument.InstrumentId.ShouldBe(_sber.Uid),
                instrument => instrument.Interval.ShouldBe(SubscriptionInterval.OneMinute));
        Stream(0)!.Headers.GetValue("authorization").ShouldBe("Bearer t.invented-token");
        updates.Current.ShouldBe(
            new FormingCandleInternal(Open, Open: 250.5m, High: 251.5m, Low: 249.5m, Close: 250.5m, Volume: 40m, IsClosed: false, Open.AddSeconds(20)));
    }

    [Test]
    public async Task SubscriptionsOfOneTokenShareOneStream_AndEachReceivesOnlyItsOwnCandles()
    {
        using IDisposable access = MarketDataAccess.Use(Credential);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<FormingCandleInternal> first = _source.Subscribe(Sber, Minute, cancel.Token).GetAsyncEnumerator(cancel.Token);
        await using IAsyncEnumerator<FormingCandleInternal> second = _source.Subscribe(Gazp, FiveMinutes, cancel.Token).GetAsyncEnumerator(cancel.Token);

        Task<bool> firstNext = first.MoveNextAsync().AsTask();
        Task<bool> secondNext = second.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 2);
        Stream(0)!.Push(Update(_gazp, Open, close: 100m, lots: 7, SubscriptionInterval.FiveMinutes));
        Stream(0)!.Push(Update(_sber, Open, close: 250m, lots: 1));

        (await firstNext, await secondNext).ShouldBe((true, true));
        first.Current.Close.ShouldBe(250m);
        second.Current.Close.ShouldBe(100m);
        second.Current.Volume.ShouldBe(7m);
        _streams.Count.ShouldBe(1);
        _api.Calls.Count(call => call.Method.EndsWith("MarketDataStream", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Test]
    public async Task TheLastSubscriptionToLeaveUnsubscribesAndClosesTheStream()
    {
        using IDisposable access = MarketDataAccess.Use(Credential);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IAsyncEnumerator<FormingCandleInternal> updates = _source.Subscribe(Sber, Minute, cancel.Token).GetAsyncEnumerator(cancel.Token);
        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 1);
        Stream(0)!.Push(Update(_sber, Open, close: 250m, lots: 1));
        await next;

        await updates.DisposeAsync();

        Stream(0)!.Requests.Select(request => request.SubscribeCandlesRequest.SubscriptionAction)
            .ShouldBe([SubscriptionAction.Subscribe, SubscriptionAction.Unsubscribe]);
        await Until(() => Stream(0)!.Disposed);
    }

    [Test]
    public async Task ALostStreamEndsItsSubscriptions_AndTheNextSubscriptionOpensANewStream()
    {
        using IDisposable access = MarketDataAccess.Use(Credential);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<FormingCandleInternal> updates = _source.Subscribe(Sber, Minute, cancel.Token).GetAsyncEnumerator(cancel.Token);
        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 1);

        Stream(0)!.Fail(MockTInvestApi.Refusal(StatusCode.Unavailable, "gone"));

        await Should.ThrowAsync<TInvestRpcException>(() => next);

        await using IAsyncEnumerator<FormingCandleInternal> again = _source.Subscribe(Sber, Minute, cancel.Token).GetAsyncEnumerator(cancel.Token);
        Task<bool> againNext = again.MoveNextAsync().AsTask();
        await Until(() => Stream(1)?.Requests.Count == 1);
        Stream(1)!.Push(Update(_sber, Open, close: 251m, lots: 1));

        (await againNext).ShouldBeTrue();
    }

    [Test]
    public async Task ARefusedSubscriptionEndsOnlyThatSubscription()
    {
        using IDisposable access = MarketDataAccess.Use(Credential);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<FormingCandleInternal> updates = _source.Subscribe(Sber, Minute, cancel.Token).GetAsyncEnumerator(cancel.Token);
        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 1);

        var refusal = new SubscribeCandlesResponse();
        refusal.CandlesSubscriptions.Add(
            new CandleSubscription { InstrumentUid = _sber.Uid, Interval = SubscriptionInterval.OneMinute, SubscriptionStatus = SubscriptionStatus.InstrumentNotFound });
        Stream(0)!.Push(new MarketDataResponse { SubscribeCandlesResponse = refusal });

        await Should.ThrowAsync<IOException>(() => next);
    }

    [Test]
    public async Task AnUpdateThatIsNotAValidExchangeCandleIsDropped()
    {
        using IDisposable access = MarketDataAccess.Use(Credential);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<FormingCandleInternal> updates = _source.Subscribe(Sber, Minute, cancel.Token).GetAsyncEnumerator(cancel.Token);
        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => Stream(0)?.Requests.Count == 1);

        MarketDataResponse inverted = Update(_sber, Open, close: 250m, lots: 1);
        inverted.Candle.High = Quotation(200m);
        MarketDataResponse misaligned = Update(_sber, Open.AddSeconds(5), close: 250m, lots: 1);
        MarketDataResponse otherSource = Update(_sber, Open, close: 250m, lots: 1);
        otherSource.Candle.CandleSourceType = CandleSource.DealerWeekend;
        Stream(0)!.Push(inverted);
        Stream(0)!.Push(misaligned);
        Stream(0)!.Push(otherSource);
        Stream(0)!.Push(Update(_sber, Open.AddMinutes(1), close: 252m, lots: 1));

        (await next).ShouldBeTrue();
        updates.Current.OpenTime.ShouldBe(Open.AddMinutes(1));
    }

    [Test]
    public async Task WithoutACredentialNothingIsSubscribed()
    {
        await using IAsyncEnumerator<FormingCandleInternal> updates = _source.Subscribe(Sber, Minute, CancellationToken.None).GetAsyncEnumerator();

        await Should.ThrowAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        _api.Calls.ShouldBeEmpty();
    }

    private MockTInvestDuplexCall<MarketDataRequest, MarketDataResponse>? Stream(int index)
    {
        lock (_streams)
        {
            return index < _streams.Count ? _streams[index] : null;
        }
    }

    private static MarketDataResponse Update(
        Share share,
        DateTimeOffset openTime,
        decimal close,
        long lots,
        SubscriptionInterval interval = SubscriptionInterval.OneMinute)
    {
        return new MarketDataResponse
        {
            Candle = new Candle
            {
                InstrumentUid = share.Uid,
                Interval = interval,
                Time = Timestamp.FromDateTimeOffset(openTime),
                LastTradeTs = Timestamp.FromDateTimeOffset(openTime.AddSeconds(20)),
                Open = Quotation(close),
                High = Quotation(close + 1m),
                Low = Quotation(close - 1m),
                Close = Quotation(close),
                Volume = lots,
                CandleSourceType = CandleSource.Exchange
            }
        };
    }

    private static Quotation Quotation(decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));

        return new Quotation { Units = units, Nano = decimal.ToInt32((value - units) * 1_000_000_000m) };
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
}