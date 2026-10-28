using Microsoft.Extensions.Time.Testing;

namespace ProfitGraph.Tests.Unit.Infrastructure.Bots;

/// <summary>
///     A fake clock that counts the timers created on it, so a test advances the clock only once the code under test waits on
///     it: a delay that starts after the clock moved would be due that much later.
/// </summary>
internal sealed class TimerCountingTimeProvider(DateTimeOffset startDateTime) : FakeTimeProvider(startDateTime)
{
    private int _timers;

    public int Timers => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);

        return base.CreateTimer(callback, state, dueTime, period);
    }
}