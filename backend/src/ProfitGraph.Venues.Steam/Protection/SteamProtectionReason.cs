namespace ProfitGraph.Venues.Steam.Protection;

/// <summary>
///     Why a Steam account's protection opened (Profit Fox Designer ADRs 0083 and 0089).
/// </summary>
internal enum SteamProtectionReason
{
    Unauthorized,
    Forbidden,
    RateLimited,
    Captcha,
    Restricted,

    /// <summary>
    ///     Unreadable answers in a row; transient noise that resumes by itself.
    /// </summary>
    InvalidResponse,

    /// <summary>
    ///     Network errors or 5xx in a row; transient noise that resumes by itself.
    /// </summary>
    TransientFailure,

    /// <summary>
    ///     Too many failures among the recent answers; transient noise that resumes by itself.
    /// </summary>
    ErrorRateExceeded
}