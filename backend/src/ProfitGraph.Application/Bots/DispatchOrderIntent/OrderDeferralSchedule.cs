namespace ProfitGraph.Application.Bots.DispatchOrderIntent;

/// <summary>
///     When a deferred submit is asked about again (ADR 0024): at the venue's recheck time, else when the wait ends, else after
///     the longest interval, never sooner than the floor so a venue cannot make the dispatcher spin.
/// </summary>
internal static class OrderDeferralSchedule
{
    internal static readonly TimeSpan Floor = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     The wait before an unreadable answer is asked again.
    /// </summary>
    internal static readonly TimeSpan UnknownRetry = TimeSpan.FromSeconds(30);

    public static DateTimeOffset NextCheck(DateTimeOffset? recheckAt, DateTimeOffset? until, DateTimeOffset now)
    {
        DateTimeOffset wanted = recheckAt ?? until ?? now + Ceiling;

        return wanted < now + Floor ? now + Floor : wanted > now + Ceiling ? now + Ceiling : wanted;
    }
}