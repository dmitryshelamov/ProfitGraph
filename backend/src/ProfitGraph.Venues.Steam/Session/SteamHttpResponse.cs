namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     What Steam answered, with the body already read within the size limit and a byte order mark removed, and Steam's own
///     result code from <c>x-eresult</c> when it sent one (1 is success). Its
///     <see cref="ToString" /> never prints cookies or the body.
/// </summary>
internal sealed record SteamHttpResponse(int StatusCode, byte[] Body, string? MediaType, IReadOnlyList<string> SetCookies, Uri? Location, int? EResult = null)
{
    public override string ToString()
    {
        return $"SteamHttpResponse {{ StatusCode = {StatusCode}, MediaType = {MediaType}, Length = {Body.Length} }}";
    }
}