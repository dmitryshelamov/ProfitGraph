using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ProfitGraph.Infrastructure.Jobs;

namespace ProfitGraph.Tests.Unit.Infrastructure.Jobs;

public sealed class BackgroundJobStopWatcherTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 28, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public async Task AFailedCheckIsRepeatedOnTheNextIntervalAndAStopRequestThenStopsTheWork()
    {
        var clock = new FakeTimeProvider(Start);
        var checks = 0;
        var firstFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BackgroundJobStopWatcher(
            _ =>
            {
                if (Interlocked.Increment(ref checks) == 1)
                {
                    firstFailed.SetResult();

                    throw new InvalidOperationException("connection lost");
                }

                return Task.FromResult((Cancel: true, Pause: false));
            },
            Interval,
            clock,
            NullLogger.Instance);
        using var work = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        Task watching = watcher.Watch(work, stop.Token);

        clock.Advance(Interval);
        await firstFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        work.IsCancellationRequested.ShouldBeFalse();
        clock.Advance(Interval);
        await watching.WaitAsync(TimeSpan.FromSeconds(5));

        checks.ShouldBe(expected: 2);
        work.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    public async Task FinishedWorkEndsTheWatchWithoutStoppingIt()
    {
        var clock = new FakeTimeProvider(Start);
        var checks = 0;
        var watcher = new BackgroundJobStopWatcher(
            _ =>
            {
                Interlocked.Increment(ref checks);

                return Task.FromResult((Cancel: false, Pause: false));
            },
            Interval,
            clock,
            NullLogger.Instance);
        using var work = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        Task watching = watcher.Watch(work, stop.Token);

        await stop.CancelAsync();
        await watching.WaitAsync(TimeSpan.FromSeconds(5));

        checks.ShouldBe(expected: 0);
        work.IsCancellationRequested.ShouldBeFalse();
    }
}