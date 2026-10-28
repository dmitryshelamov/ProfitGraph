using Microsoft.Extensions.Logging.Abstractions;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;
using ProfitGraph.Infrastructure.Bots.FormingCandles;

namespace ProfitGraph.Tests.Unit.Infrastructure.Bots;

/// <summary>
///     The shared forming-candle feeds (ADR 0026): one upstream connection per series however many clients follow it, throttling
///     that never holds a closed candle or a new interval back, and recovery with a doubling delay. No network.
/// </summary>
public sealed class FormingCandleFeedsTests
{
    private static readonly FormingFeedKeyInternal Key = new FormingFeedKeyInternal("test-venue", "BTCUSDT", "1m", CredentialId: null);
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 10, day: 26, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private FakeFormingCandleSource _source = null!;
    private RecordingFormingCandleSink _sink = null!;
    private TimerCountingTimeProvider _clock = null!;
    private FormingCandleFeeds _feeds = null!;

    [SetUp]
    public void Create()
    {
        _source = new FakeFormingCandleSource();
        _sink = new RecordingFormingCandleSink();
        _clock = new TimerCountingTimeProvider(Minute);
        _feeds = new FormingCandleFeeds([_source], _sink, new FormingCandleOptions(), _clock, NullLogger<FormingCandleFeeds>.Instance);
    }

    [TearDown]
    public async Task Release()
    {
        await _feeds.Leave("a");
        await _feeds.Leave("b");
    }

    [Test]
    public async Task ClientsOfOneSeriesShareOneUpstream_AndTheLastToLeaveClosesIt()
    {
        await _feeds.Join(Key, access: null, "a");
        await Until(() => _source.Subscriptions == 1);
        await _feeds.Join(Key, access: null, "b");

        _source.Subscriptions.ShouldBe(1);

        await _feeds.Leave("a");
        _source.Open.ShouldBe(1);

        await _feeds.Leave("b");
        await Until(() => _source.Open == 0);
    }

    [Test]
    public async Task ALateClientLearnsTheLatestCandleAndTheStateAtOnce()
    {
        FormingFeedSnapshotInternal first = await _feeds.Join(Key, access: null, "a");
        await Until(() => _source.Subscriptions == 1);
        _source.Push(Candle(Minute, close: 105m));
        await Until(() => _sink.Candles.Count == 1);

        FormingFeedSnapshotInternal late = await _feeds.Join(Key, access: null, "b");

        first.Status.ShouldBe(FormingFeedStatus.NoData);
        first.Candle.ShouldBeNull();
        late.Status.ShouldBe(FormingFeedStatus.Live);
        late.Candle!.Close.ShouldBe(105m);
        _sink.Statuses.ShouldBe([FormingFeedStatus.Live]);
    }

    [Test]
    public async Task UpdatesWithinTheThrottleWaitForTheTick_ButAClosedCandleAndANewIntervalGoAtOnce()
    {
        await _feeds.Join(Key, access: null, "a");
        await Until(() => _source.Subscriptions == 1);

        _source.Push(Candle(Minute, close: 101m));
        await Until(() => _sink.Candles.Count == 1);
        _source.Push(Candle(Minute, close: 102m));
        _source.Push(Candle(Minute, close: 103m));
        await Task.Delay(50);

        _sink.Candles.Count.ShouldBe(1);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => _sink.Candles.Count == 2);
        _sink.Candles[1].Close.ShouldBe(103m);

        _source.Push(Candle(Minute, close: 104m, closed: true));
        await Until(() => _sink.Candles.Count == 3);
        _source.Push(Candle(Minute.AddMinutes(1), close: 110m));
        await Until(() => _sink.Candles.Count == 4);

        _sink.Candles.Select(candle => (candle.OpenTime, candle.Close, candle.IsClosed))
            .ShouldBe([(Minute, 101m, false), (Minute, 103m, false), (Minute, 104m, true), (Minute.AddMinutes(1), 110m, false)]);
    }

    [Test]
    public async Task AnOlderCandleThanThePublishedOneIsDropped()
    {
        await _feeds.Join(Key, access: null, "a");
        await Until(() => _source.Subscriptions == 1);
        _source.Push(Candle(Minute.AddMinutes(1), close: 110m));
        await Until(() => _sink.Candles.Count == 1);

        _source.Push(Candle(Minute, close: 90m, closed: true));
        _clock.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        _sink.Candles.Count.ShouldBe(1);
    }

    [Test]
    public async Task ALostConnectionReportsNoData_ReconnectsAfterADoublingDelay_AndTheNextCandleIsLive()
    {
        await _feeds.Join(Key, access: null, "a");
        await Until(() => _source.Subscriptions == 1);
        _source.Push(Candle(Minute, close: 101m));
        await Until(() => _sink.Statuses.Count == 1);

        _source.Fail();
        await Until(() => _sink.Statuses.Count == 2);
        _sink.Statuses.ShouldBe([FormingFeedStatus.Live, FormingFeedStatus.NoData]);

        // The throttle's ticks are the feed's first timer; each backoff is another, created once the feed waits it out.
        await Until(() => _clock.Timers == 2);
        _clock.Advance(TimeSpan.FromMilliseconds(999));
        await Task.Delay(50);
        _source.Subscriptions.ShouldBe(1);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await Until(() => _source.Subscriptions == 2);
        _source.Fail();
        await Until(() => _source.Open == 0 && _clock.Timers == 3);

        // The second failure in a row waits twice as long.
        _clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        _source.Subscriptions.ShouldBe(2);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => _source.Subscriptions == 3);
        _source.Push(Candle(Minute, close: 102m));
        await Until(() => _sink.Statuses.Count == 3);
        _sink.Statuses[^1].ShouldBe(FormingFeedStatus.Live);
    }

    [Test]
    public async Task ASilentFeedReportsNoData_AndTheNextCandleReportsLiveAgain()
    {
        await _feeds.Join(Key, access: null, "a");
        await Until(() => _source.Subscriptions == 1);
        _source.Push(Candle(Minute, close: 101m));
        await Until(() => _sink.Statuses.Count == 1);

        for (var second = 0; second < 100; second++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        await Until(() => _sink.Statuses.Count == 2);
        _sink.Statuses[^1].ShouldBe(FormingFeedStatus.NoData);

        _source.Push(Candle(Minute, close: 102m));
        await Until(() => _sink.Statuses.Count == 3);
        _sink.Statuses[^1].ShouldBe(FormingFeedStatus.Live);
    }

    [Test]
    public async Task AConnectionFollowsOneFeedAtATime()
    {
        FormingFeedKeyInternal other = Key with { Symbol = "ETHUSDT" };
        await _feeds.Join(Key, access: null, "a");
        await Until(() => _source.Subscriptions == 1);

        await _feeds.Join(other, access: null, "a");

        await Until(() => _source.Subscriptions == 2 && _source.Open == 1);
    }

    [Test]
    public async Task TheVenueReadsWithTheCredentialOfTheFeed()
    {
        var credential = new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "test-venue", "invented-key", "invented-secret");
        await _feeds.Join(Key with { CredentialId = credential.CredentialId }, credential, "a");

        await Until(() => _source.Subscriptions == 1);

        _source.SeenAccess.ShouldBe(credential);
    }

    [Test]
    public async Task AVenueWithoutASourceIsUnsupported()
    {
        FormingFeedSnapshotInternal snapshot = await _feeds.Join(Key with { Venue = "another-venue" }, access: null, "a");

        snapshot.Status.ShouldBe(FormingFeedStatus.Unsupported);
        _source.Subscriptions.ShouldBe(0);
    }

    [TestCase(arg1: 0, arg2: 1)]
    [TestCase(arg1: 1, arg2: 2)]
    [TestCase(arg1: 3, arg2: 8)]
    [TestCase(arg1: 6, arg2: 60)]
    [TestCase(arg1: 40, arg2: 60)]
    public void BackoffDoublesUpToItsCeiling(int failures, int seconds)
    {
        new FormingCandleOptions().Backoff(failures).ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    private static FormingCandleInternal Candle(DateTimeOffset openTime, decimal close, bool closed = false)
    {
        return new FormingCandleInternal(openTime, Open: 100m, High: 120m, Low: 90m, close, Volume: 5m, closed, openTime);
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