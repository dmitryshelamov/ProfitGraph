namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     One signed-in Community session: its session ID, the cookie header and when it must be renewed. A generation tells
///     a late unauthorized answer of an older session from one of the current session.
/// </summary>
internal sealed record SteamSessionSnapshot(long Generation, string SessionId, string Cookies, DateTimeOffset ExpiresAt)
{
    public override string ToString()
    {
        return $"SteamSessionSnapshot {{ Generation = {Generation}, ExpiresAt = {ExpiresAt:O}, Redacted }}";
    }
}