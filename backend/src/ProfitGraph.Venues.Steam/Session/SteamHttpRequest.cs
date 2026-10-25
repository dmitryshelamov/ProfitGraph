namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     One request to a Steam host. A request with a form is a POST and a write unless <see cref="IsRead" /> says
///     otherwise (Steam answers some reads, such as server time, only to POST).
/// </summary>
internal sealed record SteamHttpRequest(SteamEndpoint Endpoint, string Path, IReadOnlyDictionary<string, string>? Form = null, bool IsRead = true, bool Multipart = false)
{
    public static SteamHttpRequest Get(SteamEndpoint endpoint, string path)
    {
        return new SteamHttpRequest(endpoint, path);
    }

    public static SteamHttpRequest Write(SteamEndpoint endpoint, string path, IReadOnlyDictionary<string, string> form, bool multipart = false)
    {
        return new SteamHttpRequest(endpoint, path, form, IsRead: false, multipart);
    }
}