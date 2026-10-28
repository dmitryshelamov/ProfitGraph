namespace ProfitGraph.Api.Common.Authentication;

/// <summary>
///     Section <c>Auth</c>: how long a session lasts, how often one client address may try to sign in, and how failed
///     attempts lock a login out (ADR 0043).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    ///     How long a session cookie lasts; the lifetime slides while the session is used.
    /// </summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    ///     How many sign-in requests one client address may send per <see cref="SignInWindow" />.
    /// </summary>
    public int SignInPermitLimit { get; init; } = 20;

    public TimeSpan SignInWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Consecutive failed attempts before a lockout starts.
    /// </summary>
    public int FreeFailures { get; init; } = 5;

    /// <summary>
    ///     How many client addresses a login remembers as ones it signs in from.
    /// </summary>
    public int KnownAddressesPerLogin { get; init; } = 4;

    /// <summary>
    ///     The first lockout; each later failure doubles it up to <see cref="MaximumLockout" />.
    /// </summary>
    public TimeSpan FirstLockout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaximumLockout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     How long after the last failure, with no lockout left, the failures are forgotten.
    /// </summary>
    public TimeSpan ForgetFailuresAfter { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     How often forgotten failures are removed.
    /// </summary>
    public TimeSpan FailureSweepInterval { get; init; } = TimeSpan.FromMinutes(1);

    public bool IsValid()
    {
        return SessionLifetime > TimeSpan.Zero && SignInPermitLimit > 0 && SignInWindow > TimeSpan.Zero && FreeFailures > 0 && KnownAddressesPerLogin > 0 &&
            FirstLockout > TimeSpan.Zero && MaximumLockout >= FirstLockout && ForgetFailuresAfter > TimeSpan.Zero && FailureSweepInterval > TimeSpan.Zero;
    }
}