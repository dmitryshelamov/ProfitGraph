using System.Threading.RateLimiting;

namespace ProfitGraph.Api.Common.Authentication;

/// <summary>
///     Limits sign-in attempts per login, independent of the client address.
/// </summary>
public sealed class SignInLoginLimiter(PartitionedRateLimiter<string> limiter)
{
    public RateLimitLease Acquire(string login)
    {
        return limiter.AttemptAcquire(login.Trim().ToUpperInvariant());
    }
}