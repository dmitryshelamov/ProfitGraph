using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Infrastructure.Scanner;

namespace ProfitGraph.Tests.Unit.Infrastructure.Scanner;

public sealed class VenueScanRunnerTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 10, minute: 0, second: 30, TimeSpan.Zero);

    private FakeTimeProvider _clock = null!;
    private FakeMarketScanSource _source = null!;
    private List<ReplaceScanSnapshotCommand> _snapshots = null!;
    private ServiceProvider _services = null!;
    private CancellationTokenSource _stopping = null!;
    private Task _running = null!;

    [SetUp]
    public void StartRunner()
    {
        _clock = new FakeTimeProvider(Start);
        _source = new FakeMarketScanSource("BTCUSDT");
        _snapshots = [];
        var sender = Substitute.For<ISender>();
        sender.When(mock => mock.Send(Arg.Any<ReplaceScanSnapshotCommand>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                lock (_snapshots)
                {
                    _snapshots.Add(call.Arg<ReplaceScanSnapshotCommand>()!);
                }
            });
        _services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        _stopping = new CancellationTokenSource();

        var runner = new VenueScanRunner(
            _source,
            _services.GetRequiredService<IServiceScopeFactory>(),
            new ScannerOptions(),
            _clock,
            NullLogger.Instance);
        _running = runner.Run(_stopping.Token);
    }

    [TearDown]
    public async Task StopRunner()
    {
        await _stopping.CancelAsync();
        await _running;
        _stopping.Dispose();
        await _services.DisposeAsync();
    }

    [Test]
    public async Task SubscribesBeforeReadingHistoryAndThenWritesASnapshotEveryMinute()
    {
        await Eventually(() => _source.Calls.Contains("stream BTCUSDT"));
        _source.Push(Forming(Start));
        await Eventually(() => _source.Calls.Count(call => call.StartsWith("candles", StringComparison.Ordinal)) == 2);

        _source.Calls.ShouldBe(["universe", "stream BTCUSDT", "candles BTCUSDT 1m 10:00-10:00", "candles BTCUSDT 1h 10:00-10:00"]);

        // The snapshot is due five seconds after the minute closes; the minute 10:00 had no trades, so the oldest one leaves the day.
        _clock.Advance(TimeSpan.FromSeconds(35));
        await Eventually(() => Snapshots().Count == 1);

        ScanSnapshotInternal snapshot = Snapshots()[0].Snapshot;
        snapshot.Venue.ShouldBe("bybit");
        snapshot.ComputedAt.ShouldBe(Start.AddSeconds(35));
        ScanSnapshotRowInternal row = snapshot.Rows.Single();
        row.Symbol.ShouldBe("BTCUSDT");
        row.Incomplete.ShouldBeFalse();
        row.Metrics.Turnover24h.ShouldBe(1439m);
        row.Metrics.Spread.ShouldBe(2m);
    }

    [Test]
    public async Task AReconnectBackfillsFromTheLastMinute()
    {
        await Eventually(() => _source.Calls.Contains("stream BTCUSDT"));
        _source.Push(Forming(Start));
        await Eventually(() => _source.Calls.Count(call => call.StartsWith("candles", StringComparison.Ordinal)) == 2);

        _source.Drop();

        // The stream reconnects after its one-second pause.
        await Eventually(() =>
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));

            return _source.Calls.Count(call => call.StartsWith("stream", StringComparison.Ordinal)) == 2;
        });

        // Three minutes pass before the backfill.
        _clock.Advance(TimeSpan.FromMinutes(3));
        _source.Push(Forming(_clock.GetUtcNow()));

        await Eventually(() => _source.Calls.Contains("candles BTCUSDT 1m 10:00-10:03"));
    }

    [Test]
    public async Task AStreamThatOnlyReportsItselfAliveIsSubscribedAndStaysConnected()
    {
        // A closed market sends no candles, only the venue's pings (ADR 0062).
        await Eventually(() => _source.Calls.Contains("stream BTCUSDT"));
        _source.Push(ScanCandleUpdateInternal.Alive);
        await Eventually(() => _source.Calls.Count(call => call.StartsWith("candles", StringComparison.Ordinal)) == 2);

        for (var step = 0; step < 18; step++)
        {
            _clock.Advance(TimeSpan.FromSeconds(10));
            _source.Push(ScanCandleUpdateInternal.Alive);
            await Task.Delay(millisecondsDelay: 20);
        }

        _source.Calls.Count(call => call.StartsWith("stream", StringComparison.Ordinal)).ShouldBe(1);
        await Eventually(() => Snapshots().Count > 0);
        Snapshots()[^1].Snapshot.Rows.Single().Metrics.Stale.ShouldBeFalse();
    }

    private static ScanCandleUpdateInternal Forming(DateTimeOffset now)
    {
        var minute = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);

        return new ScanCandleUpdateInternal("BTCUSDT", new ScanCandle(minute, Open: 100, High: 100, Low: 100, Close: 100, Volume: 1, Turnover: 1), Closed: false);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(millisecondsDelay: 10);
        }

        condition().ShouldBeTrue();
    }

    private List<ReplaceScanSnapshotCommand> Snapshots()
    {
        lock (_snapshots)
        {
            return [.. _snapshots];
        }
    }
}