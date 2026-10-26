using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Bybit.LiveData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The forming candle from recorded-shape public kline messages (ADR 0026); no network and no credentials.
/// </summary>
public sealed class BybitFormingCandleSourceTests
{
    private const string Topic = "kline.60.BTCUSDT";

    private static readonly InstrumentSymbol Symbol = new InstrumentSymbol("BTCUSDT");
    private static readonly Timeframe Hour = Timeframe.Parse("1h");
    private static readonly DateTimeOffset First = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void ConfirmedAndFormingCandlesAreBothReadWithTheCloseFlag()
    {
        BybitKlineMessages.Updates(FakePublicStreamTransport.Kline(Topic, First, close: 105m, confirm: false), Topic, Hour)
            .ShouldBe([new FormingCandleInternal(First, Open: 100m, High: 110m, Low: 90m, Close: 105m, Volume: 5m, IsClosed: false, First)]);
        BybitKlineMessages.Updates(FakePublicStreamTransport.Kline(Topic, First, close: 105m), Topic, Hour).Single().IsClosed.ShouldBeTrue();
    }

    [Test]
    public void TheMessageTimeIsWhenTheCandleWasObserved()
    {
        var message = """
                      {"topic":"kline.60.BTCUSDT","type":"snapshot","ts":1792926000123,"data":[{"start":1792922400000,"open":"100","high":"110","low":"90","close":"105","volume":"5","confirm":false}]}
                      """;

        BybitKlineMessages.Updates(message, Topic, Hour).Single().ObservedAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1792926000123));
    }

    [Test]
    public void OtherTopicsAndMessagesAreIgnored_AndAnInvalidCandleIsRejected()
    {
        BybitKlineMessages.Updates(FakePublicStreamTransport.Kline("kline.60.ETHUSDT", First, close: 105m), Topic, Hour).ShouldBeEmpty();
        BybitKlineMessages.Updates(message: """{"success":true,"op":"subscribe"}""", Topic, Hour).ShouldBeEmpty();
        Should.Throw<InvalidDataException>(() => BybitKlineMessages.Updates(FakePublicStreamTransport.Kline(Topic, First.AddMinutes(1), close: 105m), Topic, Hour));
        Should.Throw<InvalidDataException>(() => BybitKlineMessages.Updates(FakePublicStreamTransport.Kline(Topic, First, close: 200m), Topic, Hour));
    }

    [TestCase("1m", arg2: true)]
    [TestCase("4h", arg2: true)]
    [TestCase("1d", arg2: true)]
    public void SupportsEveryTimeframeOfTheSystem(string timeframe, bool supported)
    {
        new BybitFormingCandleSource(new FakePublicStreamTransport()).Supports(Timeframe.Parse(timeframe)).ShouldBe(supported);
    }

    [Test]
    public async Task ASubscriptionStreamsEveryUpdateOfItsTopic_AndStopsReadingWhenCancelled()
    {
        var transport = new FakePublicStreamTransport();
        var source = new BybitFormingCandleSource(transport);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<FormingCandleInternal> updates = source.Subscribe(Symbol, Hour, cancel.Token).GetAsyncEnumerator(cancel.Token);

        Task<bool> next = updates.MoveNextAsync().AsTask();
        await Until(() => transport.Connections == 1);
        transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 101m, confirm: false));
        (await next).ShouldBeTrue();
        updates.Current.Close.ShouldBe(101m);

        Task<bool> closing = updates.MoveNextAsync().AsTask();
        transport.Push(FakePublicStreamTransport.Kline(Topic, First, close: 102m));
        (await closing).ShouldBeTrue();
        updates.Current.IsClosed.ShouldBeTrue();
        transport.LastTopic.ShouldBe(Topic);
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