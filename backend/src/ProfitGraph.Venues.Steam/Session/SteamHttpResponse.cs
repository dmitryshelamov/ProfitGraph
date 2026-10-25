namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     What Steam answered, with the body already read within the size limit and a byte order mark removed. Its
///     <see cref="ToString" /> never prints cookies or the body.
/// </summary>
internal sealed record SteamHttpResponse(int StatusCode, byte[] Body, string? MediaType, IReadOnlyList<string> SetCookies, Uri? Location)
{
    public override string ToString()
    {
        return $"SteamHttpResponse {{ StatusCode = {StatusCode}, MediaType = {MediaType}, Length = {Body.Length} }}";
    }
}