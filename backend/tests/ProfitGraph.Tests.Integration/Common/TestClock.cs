namespace ProfitGraph.Tests.Integration.Common;

/// <summary>
///     A clock the test moves by hand.
/// </summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new DateTimeOffset(year: 2026, month: 1, day: 2, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}