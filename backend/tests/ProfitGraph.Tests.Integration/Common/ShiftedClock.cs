namespace ProfitGraph.Tests.Integration.Common;

/// <summary>
///     The system clock shifted by an offset the test moves forward, so a scenario can pass Steam's minute-precise unlock
///     times without waiting; timers and timestamps stay the system's.
/// </summary>
public sealed class ShiftedClock : TimeProvider
{
    private long _offsetTicks;

    public void Advance(TimeSpan by)
    {
        Interlocked.Add(ref _offsetTicks, by.Ticks);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
    }
}