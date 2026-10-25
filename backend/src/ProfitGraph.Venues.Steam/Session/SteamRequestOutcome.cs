namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     How a Steam request ended, before any caller reads its body (Profit Fox Designer ADR 0083). A write that may have
///     reached Steam and has no clear answer is <see cref="Unknown" />, never a rejection.
/// </summary>
internal enum SteamRequestOutcome
{
    Success,

    /// <summary>
    ///     HTTP 401, a redirect to the login page or <c>needauth</c>: the session is no longer valid.
    /// </summary>
    Unauthorized,

    Forbidden,

    /// <summary>
    ///     Steam asks for a CAPTCHA; nothing may bypass it.
    /// </summary>
    Captcha,

    /// <summary>
    ///     Steam restricts the account's market use.
    /// </summary>
    Restricted,

    /// <summary>
    ///     HTTP 429; the caller waits at least <see cref="SteamRequestResult.RetryAfter" />.
    /// </summary>
    RateLimited,

    /// <summary>
    ///     Another 4xx; the body may still explain it, as Steam's write replies often do.
    /// </summary>
    ClientError,

    /// <summary>
    ///     A read failed in a way that may pass: a network error, a timeout or a 5xx.
    /// </summary>
    TransientFailure,

    /// <summary>
    ///     A read answered something unreadable: an unexpected redirect, an empty or oversized body.
    /// </summary>
    InvalidResponse,

    /// <summary>
    ///     A write may or may not have taken effect.
    /// </summary>
    Unknown,

    /// <summary>
    ///     ProfitGraph refused to send: Steam is disabled, or the credential cannot be used.
    /// </summary>
    NotSent,

    /// <summary>
    ///     The request waited in the account queue past its deadline and was never sent.
    /// </summary>
    Deferred
}