using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Bots.MarkFeedStale;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Bybit.LiveData;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     Live klines from recorded-shape public messages; no network (Profit Fox Designer ADR 0054).
/// </summary>
public sealed class BybitLiveCandleSourceTests
{
    private const string Topic = "kline.60.BTCUSDT";

    private static readonly InstrumentSymbol Symbol = new InstrumentSymbol("BTCUSDT");
    private static readonly Timeframe Hour = Timeframe.Parse("1h");
    private static readonly DateTimeOffset First = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 10, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private FakePublicStreamTransport _transport = null!;
    private FakeBybitHistory _history = null!;
    private ISender _sender = null!;
    private FakeTimeProvider _clock = null!;
    private BybitLiveCandleSource _source = null!;

    [SetUp]
    public void Create()
    {
        _transport = new FakePublicStreamTransport();
        _history = new FakeBybitHistory();
        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<MarkFeedStaleCommand>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _clock = new FakeTimeProvider(First.AddHours(1).AddSeconds(5));
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IHistoricalCandleSource>(_history)
            .AddSingleton(_sender)
            .BuildServiceProvider();
        _source = new BybitLiveCandleSource(
            _transport,
            services.GetRequiredService<IServiceScopeFactory>(),
            new BybitOptions(),
            _clock,
            NullLogger<BybitLiveCandleSource>.Instance);
    }

    [Test]
    public void TopicNamesTheIntervalAndSymbol()
    {
        BybitKlineMessages.Topic("BTCUSDT", Hour).ShouldBe(Topic);
        BybitKlineMessages.Topic("ETHUSDT", Timeframe.Parse("1d")).ShouldBe("kline.D.ETHUSDT");
        BybitKlineMessages.Topic("ETHUSDT", Timeframe.Parse("15m")).ShouldBe("kline.15.ETHUSDT");
    }

    [Test]
    public void OnlyConfirmedCandlesOfTheTopicAreRead()
    {
        BybitKlineMessages.Closed(FakePublicStreamTransport.Kline(Topic, First, close: 105m), Topic, Hour)
            .ShouldBe([new Candle(First, Open: 100m, High: 110m, Low: 90m, Close: 105m, Volume: 5m)]);
        BybitKlineMessages.Closed(FakePublicStreamTransport.Kline(Topic, First, close: 105m, confirm: false), Topic, Hour).ShouldBeEmpty();
        BybitKlineMessages.Closed(FakePublicStreamTransport.Kline("kline.60.ETHUSDT", First, close: 105m), Topic, Hour).ShouldBeEmpty();
        BybitKlineMessages.Closed(message: """{"success":true,"op":"subscribe"}""", Topic, Hour).ShouldBeEmpty();
    }

    [Test]
    public void MisalignedOrInvalidCandleIsRejected()
    {
        Should.Throw<InvalidDataException>(() => BybitKlineMessages.Closed(FakePublicStreamTransport.Kline(Topic, First.AddMinutes(1), close: 105m), Topic, Hour));
        Should.Throw<InvalidDataException>(() => BybitKlineMessages.Closed(FakePublicStreamTransport.Kline(Topic, First, close: 200m), Topic, Hour));
    }

    [Test]
    public async Task SubscriberReceivesEachConfirmedCandleOnceInOrder()
    {
        using var cancellation = new CancellationTokenSource(Wait);
        Task<List<Candle>> received = Take(_source.Subscribe(Symbol, Hour, afterOpen: null, cancellation.Token), count: 2);
        await Connected(connections: 1);

        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 101m, confirm: false));
        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 102m));
        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 103m));
        _transport.Push(FakePublicStreamTransport.Kline(Topic, First.AddHours(1), close: 104m));

        (await received).Select(candle => (candle.OpenTime, candle.Close)).ShouldBe([(First, 102m), (First.AddHours(1), 104m)]);
    }

    [Test]
    public async Task SubscribersOfOneSeriesShareOneConnection()
    {
        using var cancellation = new CancellationTokenSource(Wait);
        Task<List<Candle>> first = Take(_source.Subscribe(Symbol, Hour, afterOpen: null, cancellation.Token), count: 1);
        await Connected(connections: 1);
        Task<List<Candle>> second = Take(_source.Subscribe(Symbol, Hour, afterOpen: null, cancellation.Token), count: 1);
        await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 102m));

        (await first).Single().OpenTime.ShouldBe(First);
        (await second).Single().OpenTime.ShouldBe(First);
        _transport.Connections.ShouldBe(1);
        _transport.LastTopic.ShouldBe(Topic);
    }

    [Test]
    public async Task SubscriberAfterItsLastCandleGetsTheGapFromHistoryFirst()
    {
        using var cancellation = new CancellationTokenSource(Wait);
        Task<List<Candle>> received = Take(_source.Subscribe(Symbol, Hour, First.AddHours(-3), cancellation.Token), count: 3);
        await Connected(connections: 1);

        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 102m));

        (await received).Select(candle => (candle.OpenTime, candle.Close)).ShouldBe([(First.AddHours(-2), 101m), (First.AddHours(-1), 101m), (First, 102m)]);
    }

    [Test]
    public async Task SubscriberSkipsCandlesItAlreadyHas()
    {
        using var cancellation = new CancellationTokenSource(Wait);
        Task<List<Candle>> received = Take(_source.Subscribe(Symbol, Hour, First, cancellation.Token), count: 1);
        await Connected(connections: 1);

        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 102m));
        _transport.Push(FakePublicStreamTransport.Kline(Topic, First.AddHours(1), close: 104m));

        (await received).Single().OpenTime.ShouldBe(First.AddHours(1));
        _history.Reads.ShouldBe(0);
    }

    [Test]
    public async Task SilentFeedReportsItselfStaleFillsTheGapAndRecovers()
    {
        using var cancellation = new CancellationTokenSource(Wait);
        Task<List<Candle>> received = Take(_source.Subscribe(Symbol, Hour, afterOpen: null, cancellation.Token), count: 2);
        await Connected(connections: 1);
        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 102m));
        await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        _clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(61));

        (await received).Select(candle => candle.OpenTime).ShouldBe([First, First.AddHours(1)]);
        await Until(() => _sender.ReceivedCalls().Count() >= 2);
        await _sender.Received(1).Send(new MarkFeedStaleCommand("bybit", "BTCUSDT", "1h", Stale: true), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(new MarkFeedStaleCommand("bybit", "BTCUSDT", "1h", Stale: false), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LastSubscriberLeavingStopsTheFeed()
    {
        using (var cancellation = new CancellationTokenSource())
        {
            Task<List<Candle>> received = Take(_source.Subscribe(Symbol, Hour, afterOpen: null, cancellation.Token), count: 1);
            await Connected(connections: 1);
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => received);
        }

        using var again = new CancellationTokenSource(Wait);
        Task<List<Candle>> next = Take(_source.Subscribe(Symbol, Hour, afterOpen: null, again.Token), count: 1);
        await Connected(connections: 2);
        _transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 102m));

        (await next).Single().OpenTime.ShouldBe(First);
    }

    private static async Task<List<Candle>> Take(IAsyncEnumerable<Candle> candles, int count)
    {
        var taken = new List<Candle>();

        await foreach (Candle candle in candles)
        {
            taken.Add(candle);

            if (taken.Count == count)
            {
                break;
            }
        }

        return taken;
    }

    private Task Connected(int connections)
    {
        return Until(() => _transport.Connections >= connections);
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Wait;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The condition was not reached in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }
}