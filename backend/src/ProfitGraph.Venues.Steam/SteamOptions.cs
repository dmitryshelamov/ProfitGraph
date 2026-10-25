using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam;

/// <summary>
///     Section <c>Venues:Steam</c>. Steam stays off unless the owner turns it on: no request leaves the process while
///     <see cref="Enabled" /> is false, and every origin is empty until configured. Each origin accepts only its official
///     HTTPS host, or an invented <c>.invalid</c> host in tests (ADR 0022). Every duration is a local, unconfirmed policy.
/// </summary>
public sealed class SteamOptions
{
    public const string SectionName = "Venues:Steam";

    public bool Enabled { get; init; }

    public string CommunityBaseAddress { get; init; } = string.Empty;

    public string LoginBaseAddress { get; init; } = string.Empty;

    public string ApiBaseAddress { get; init; } = string.Empty;

    /// <summary>
    ///     The largest response body read; a longer one is an invalid response.
    /// </summary>
    public int MaximumResponseBytes { get; init; } = 8 * 1024 * 1024;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     How long a session is used when its cookie carries no readable expiry.
    /// </summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     How long before its expiry a session is renewed.
    /// </summary>
    public TimeSpan SessionRenewalLead { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan ServerClockRefresh { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     How long a request may wait in its account queue before it is given up unsent.
    /// </summary>
    public TimeSpan QueueDeadline { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     The least time between two writes of one account.
    /// </summary>
    public TimeSpan WriteSpacing { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The first pause after a rate limit; each further one doubles it up to <see cref="MaximumCooldown" />. A longer
    ///     Retry-After always wins.
    /// </summary>
    public TimeSpan InitialCooldown { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaximumCooldown { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Retries of a read that failed transiently, after one and then two seconds; writes and rate limits never retry.
    /// </summary>
    public int ReadRetries { get; init; } = 2;

    /// <summary>
    ///     Consecutive refusals of one kind (unauthorized, forbidden, rate limited, transient or unreadable) that open the
    ///     account's protection.
    /// </summary>
    public int ProtectionThreshold { get; init; } = 3;

    /// <summary>
    ///     The share of failures among the last <see cref="ProtectionErrorSample" /> answers that opens the protection.
    /// </summary>
    public double ProtectionErrorRatio { get; init; } = 0.5;

    public int ProtectionErrorSample { get; init; } = 10;

    /// <summary>
    ///     How long a protection opened by transient noise waits before it resumes by itself, times the automatic resumes
    ///     already used in <see cref="SoftResumeWindow" /> plus one (Profit Fox Designer ADR 0089).
    /// </summary>
    public TimeSpan SoftResumeDelay { get; init; } = TimeSpan.FromMinutes(5);

    public int SoftResumeLimit { get; init; } = 3;

    public TimeSpan SoftResumeWindow { get; init; } = TimeSpan.FromHours(6);

    public bool IsValid()
    {
        (string Address, string Host)[] origins =
        [
            (CommunityBaseAddress, "steamcommunity.com"),
            (LoginBaseAddress, "login.steampowered.com"),
            (ApiBaseAddress, "api.steampowered.com")
        ];
        bool timings = MaximumResponseBytes is >= 1024 and <= 64 * 1024 * 1024 && RequestTimeout > TimeSpan.Zero && SessionLifetime > SessionRenewalLead &&
            SessionRenewalLead >= TimeSpan.Zero && ServerClockRefresh > TimeSpan.Zero && QueueDeadline > TimeSpan.Zero && WriteSpacing >= TimeSpan.Zero &&
            InitialCooldown > TimeSpan.Zero && MaximumCooldown >= InitialCooldown && ReadRetries is >= 0 and <= 5 && ProtectionThreshold > 0 &&
            ProtectionErrorRatio is > 0 and <= 1 && ProtectionErrorSample > 0 && SoftResumeDelay > TimeSpan.Zero && SoftResumeLimit >= 0 &&
            SoftResumeWindow > TimeSpan.Zero;

        return timings && origins.All(origin => Enabled
            ? IsAllowedOrigin(origin.Address, origin.Host)
            : origin.Address.Length == 0 || IsAllowedOrigin(origin.Address, origin.Host));
    }

    internal Uri Address(SteamEndpoint endpoint)
    {
        string address = endpoint switch
        {
            SteamEndpoint.Community => CommunityBaseAddress,
            SteamEndpoint.Login => LoginBaseAddress,
            _ => ApiBaseAddress
        };

        return new Uri(address.EndsWith('/') ? address : address + "/");
    }

    private static bool IsAllowedOrigin(string address, string officialHost)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out Uri? origin) &&
            origin.Scheme == Uri.UriSchemeHttps &&
            origin.IsDefaultPort &&
            origin.UserInfo.Length == 0 &&
            origin.AbsolutePath == "/" &&
            origin.Query.Length == 0 &&
            origin.Fragment.Length == 0 &&
            (origin.Host == officialHost || origin.Host.EndsWith(".invalid", StringComparison.Ordinal));
    }
}