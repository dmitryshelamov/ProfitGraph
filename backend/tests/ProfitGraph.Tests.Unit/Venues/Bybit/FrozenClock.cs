namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     An application clock stuck in the past; Bybit's resets must not depend on it.
/// </summary>
internal sealed class FrozenClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UnixEpoch;
    }
}