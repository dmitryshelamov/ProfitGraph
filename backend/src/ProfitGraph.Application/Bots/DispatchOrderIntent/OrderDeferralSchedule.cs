namespace ProfitGraph.Application.Bots.DispatchOrderIntent;

/// <summary>
///     When a deferred submit is asked about again (ADR 0024): at the venue's recheck time, else when the wait ends, else after
///     the longest interval, never sooner than the floor so a venue cannot make the dispatcher spin.
/// </summary>
internal static class OrderDeferralSchedule
{
    public static DateTimeOffset NextCheck(DateTimeOffset? recheckAt, DateTimeOffset? until, DateTimeOffset now, OrderDispatchOptions options)
    {
        DateTimeOffset floor = now + options.DeferralFloor;
        DateTimeOffset ceiling = now + options.DeferralCeiling;
        DateTimeOffset wanted = recheckAt ?? until ?? ceiling;

        return wanted < floor ? floor : wanted > ceiling ? ceiling : wanted;
    }
}