namespace ProfitGraph.Tests.Unit.Common;

/// <summary>
///     A clock that always returns the given instant.
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}