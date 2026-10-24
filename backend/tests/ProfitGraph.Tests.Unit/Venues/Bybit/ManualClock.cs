namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     A clock a test moves by hand.
/// </summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}