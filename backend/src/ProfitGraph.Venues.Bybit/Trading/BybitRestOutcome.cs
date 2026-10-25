namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     What a signed Bybit request proved. Only <see cref="Rejected" /> is definitive for a submit; an unknown outcome,
///     a timeout, a rate limit or a server error never becomes a rejection.
/// </summary>
internal enum BybitRestOutcome
{
    Accepted,
    Rejected,
    Duplicate,
    NotFound,
    Unknown
}
