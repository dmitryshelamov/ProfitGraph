using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The account queue on a fake clock (Profit Fox Designer ADR 0083, problems 2 to 4).
/// </summary>
public sealed class SteamRequestSchedulerTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private readonly List<string> _started = new List<string>();
    private FakeTimeProvider _clock = null!;
    private SteamRequestScheduler _scheduler = null!;

    [SetUp]
    public void CreateScheduler()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero));
        _scheduler = new SteamRequestScheduler(Options.Create(new SteamOptions()), _clock);

        lock (_started)
        {
            _started.Clear();
        }
    }

    [Test]
    public async Task AnAccountSendsOneRequestAtATime()
    {
        var first = new TaskCompletionSource<SteamRequestResult>();
        Task<SteamRequestResult> a = Run("a", SteamRequestClass.Status, _ => first.Task);
        await Eventually(() => Started("a"));
        Task<SteamRequestResult> b = Run("b", SteamRequestClass.Status, Success);
        await Task.Delay(millisecondsDelay: 50);

        Started("b").ShouldBeFalse();
        first.SetResult(new SteamRequestResult(SteamRequestOutcome.Success));
        await Task.WhenAll(a, b);
        _started.ShouldBe(["a", "b"]);
    }

    [Test]
    public async Task AHigherClassGoesFirstAndHistoryWaitsForEverythingElse()
    {
        var first = new TaskCompletionSource<SteamRequestResult>();
        Task<SteamRequestResult> blocker = Run("status", SteamRequestClass.Status, _ => first.Task);
        await Eventually(() => Started("status"));
        Task<SteamRequestResult> history = Run("history", SteamRequestClass.History, Success);
        Task<SteamRequestResult> quote = Run("quote", SteamRequestClass.Quote, Success);
        Task<SteamRequestResult> write = Run("write", SteamRequestClass.Write, Success);
        await Task.Delay(millisecondsDelay: 50);

        first.SetResult(new SteamRequestResult(SteamRequestOutcome.Success));
        await Task.WhenAll(blocker, history, quote, write);

        _started.ShouldBe(["status", "write", "quote", "history"]);
    }

    [Test]
    public async Task WritesAreSpacedApart()
    {
        await Run("first", SteamRequestClass.Write, Success);
        Task<SteamRequestResult> second = Run("second", SteamRequestClass.Write, Success);
        await Task.Delay(millisecondsDelay: 50);

        Started("second").ShouldBeFalse();
        _clock.Advance(TimeSpan.FromSeconds(5));
        await second;
        Started("second").ShouldBeTrue();
    }

    [Test]
    public async Task ABackgroundRateLimitPausesOnlyQuotesAndHistory()
    {
        await Run("history", SteamRequestClass.History, RateLimited());
        Task<SteamRequestResult> quote = Run("quote", SteamRequestClass.Quote, Success);
        await Run("status", SteamRequestClass.Status, Success);
        await Task.Delay(millisecondsDelay: 50);

        Started("quote").ShouldBeFalse();
        _scheduler.Queue(Account).AccountPausedUntil.ShouldBeLessThan(_clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromMinutes(5));
        (await quote).Outcome.ShouldBe(SteamRequestOutcome.Deferred);
    }

    [Test]
    public async Task AnAccountRateLimitPausesEverythingAndHonorsALongerRetryAfter()
    {
        await Run("write", SteamRequestClass.Write, RateLimited(TimeSpan.FromHours(2)));

        _scheduler.Queue(Account).AccountPausedUntil.ShouldBe(_clock.GetUtcNow() + TimeSpan.FromHours(2));
        await Run("other", SteamRequestClass.Status, Success, Guid.NewGuid());
        Started("other").ShouldBeTrue();
    }

    [Test]
    public async Task PausesGrowAndAreCapped()
    {
        await Run("first", SteamRequestClass.Status, RateLimited());
        _scheduler.Queue(Account).AccountPausedUntil.ShouldBe(_clock.GetUtcNow() + TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Run("second", SteamRequestClass.Status, RateLimited());

        _scheduler.Queue(Account).AccountPausedUntil.ShouldBe(_clock.GetUtcNow() + TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task AQueuedRequestIsGivenUpUnsentAfterItsDeadline()
    {
        await Run("limited", SteamRequestClass.Status, RateLimited());
        Task<SteamRequestResult> waiting = Run("waiting", SteamRequestClass.Status, Success);
        await Task.Delay(millisecondsDelay: 50);

        _clock.Advance(TimeSpan.FromSeconds(46));

        (await waiting).Outcome.ShouldBe(SteamRequestOutcome.Deferred);
        Started("waiting").ShouldBeFalse();
    }

    [Test]
    public async Task OnlyATransientReadIsRetriedTwice()
    {
        var reads = 0;
        Task<SteamRequestResult> read = _scheduler.Execute(
            Account,
            SteamHttpRequest.Get(SteamEndpoint.Community, "/market/"),
            _ =>
            {
                Interlocked.Increment(ref reads);

                return Task.FromResult(new SteamRequestResult(SteamRequestOutcome.TransientFailure));
            },
            CancellationToken.None);
        await Eventually(() => Volatile.Read(ref reads) == 1);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => Volatile.Read(ref reads) == 2);
        _clock.Advance(TimeSpan.FromSeconds(2));

        (await read).Outcome.ShouldBe(SteamRequestOutcome.TransientFailure);
        reads.ShouldBe(3);

        var writes = 0;
        await _scheduler.Execute(
            Account,
            SteamHttpRequest.Write(SteamEndpoint.Community, "/market/createbuyorder/", new Dictionary<string, string>()),
            _ =>
            {
                writes++;

                return Task.FromResult(new SteamRequestResult(SteamRequestOutcome.Unknown));
            },
            CancellationToken.None);
        writes.ShouldBe(1);
    }

    private static Func<CancellationToken, Task<SteamRequestResult>> RateLimited(TimeSpan? retryAfter = null)
    {
        return _ => Task.FromResult(new SteamRequestResult(SteamRequestOutcome.RateLimited, RetryAfter: retryAfter));
    }

    private static Task<SteamRequestResult> Success(CancellationToken _)
    {
        return Task.FromResult(new SteamRequestResult(SteamRequestOutcome.Success));
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(millisecondsDelay: 10);
        }

        condition().ShouldBeTrue();
    }

    private bool Started(string name)
    {
        lock (_started)
        {
            return _started.Contains(name);
        }
    }

    private Task<SteamRequestResult> Run(
        string name,
        SteamRequestClass requestClass,
        Func<CancellationToken, Task<SteamRequestResult>> send,
        Guid? account = null)
    {
        return _scheduler.Execute(
            account ?? Account,
            new SteamHttpRequest(SteamEndpoint.Community, "/market/", Class: requestClass, IsRead: requestClass != SteamRequestClass.Write),
            token =>
            {
                lock (_started)
                {
                    _started.Add(name);
                }

                return send(token);
            },
            CancellationToken.None);
    }
}