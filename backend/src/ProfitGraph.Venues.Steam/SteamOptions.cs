namespace ProfitGraph.Venues.Steam;

/// <summary>
///     Section <c>Venues:Steam</c>. Steam stays off unless the owner turns it on: no request leaves the process while
///     <see cref="Enabled" /> is false, and the community origin is empty until configured. Only the official HTTPS host
///     is accepted, or an invented <c>.invalid</c> host in tests (ADR 0022).
/// </summary>
public sealed class SteamOptions
{
    public const string SectionName = "Venues:Steam";

    private const string OfficialHost = "steamcommunity.com";

    public bool Enabled { get; init; }

    public string CommunityBaseAddress { get; init; } = string.Empty;

    public bool IsValid()
    {
        if (!Enabled)
        {
            return CommunityBaseAddress.Length == 0 || IsAllowedOrigin(CommunityBaseAddress);
        }

        return IsAllowedOrigin(CommunityBaseAddress);
    }

    private static bool IsAllowedOrigin(string address)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out Uri? origin) &&
            origin.Scheme == Uri.UriSchemeHttps &&
            origin.IsDefaultPort &&
            origin.UserInfo.Length == 0 &&
            origin.AbsolutePath == "/" &&
            origin.Query.Length == 0 &&
            origin.Fragment.Length == 0 &&
            (origin.Host == OfficialHost || origin.Host.EndsWith(".invalid", StringComparison.Ordinal));
    }
}