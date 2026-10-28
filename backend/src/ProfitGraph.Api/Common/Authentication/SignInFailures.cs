namespace ProfitGraph.Api.Common.Authentication;

/// <summary>
///     Consecutive failed sign-in attempts of one login, or of one login from one client address, and the lockout they
///     earned.
/// </summary>
internal sealed class SignInFailures
{
    public int Count { get; private set; }

    public DateTimeOffset LastFailureAt { get; private set; }

    public DateTimeOffset LockedUntil { get; private set; }

    public TimeSpan Remaining(DateTimeOffset now)
    {
        return LockedUntil > now ? LockedUntil - now : TimeSpan.Zero;
    }

    public void Fail(DateTimeOffset now, AuthOptions options)
    {
        Count++;
        LastFailureAt = now;

        if (Count >= options.FreeFailures)
        {
            LockedUntil = now + SignInThrottle.Lockout(Count, options);
        }
    }
}