namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     What a failed Invest API call means for the caller. Only <see cref="Rejected" /> carries a broker code, read from a
///     status description that is exactly a number; broker prose is never searched.
/// </summary>
internal enum TInvestFailureKind
{
    Rejected,
    Unauthenticated,
    PermissionDenied,
    NotFound,
    RateLimited,
    Timeout,
    Unavailable,
    Unknown
}