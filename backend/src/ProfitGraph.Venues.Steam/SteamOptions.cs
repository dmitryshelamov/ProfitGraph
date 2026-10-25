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

    /// <summary>
    ///     The Steam credential whose session reads market data: price history is only served signed in, in that wallet's
    ///     currency (ADR 0022). Without one, Steam has no history.
    /// </summary>
    public Guid? MarketDataCredentialId { get; init; }

    /// <summary>
    ///     How long after its end a period counts as closed in Steam's history.
    /// </summary>
    public TimeSpan CandleCloseDelay { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>
    ///     How long a read history of an item is reused; one response holds the item's whole history.
    /// </summary>
    public TimeSpan HistoryReuse { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan WalletReuse { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The App ID an item search uses when the query names none (730 is Counter-Strike 2).
    /// </summary>
    public int DefaultAppId { get; init; } = 730;

    /// <summary>
    ///     How often the display price of an item is read; Steam has no price stream and a small request budget.
    /// </summary>
    public TimeSpan MarkPriceInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Items per inventory page (Profit Fox read 2000) and the most pages read of one context; an inventory that does not
    ///     end within them is incomplete, and nothing is listed from it.
    /// </summary>
    public int InventoryPageSize { get; init; } = 2000;

    public int InventoryMaximumPages { get; init; } = 20;

    /// <summary>
    ///     Steam <c>success</c> codes the owner confirmed as a definite refusal of a market write. None is confirmed, so by
    ///     default every refusal is unknown and waits for reconciliation (Profit Fox Designer kept the same list empty).
    /// </summary>
    public int[] DefiniteRejectionCodes { get; init; } = [];

    /// <summary>
    ///     The most pages of 100 listings read of one account; an account with more is not read, and nothing is decided from part
    ///     of its listings.
    /// </summary>
    public int ListingPagesMaximum { get; init; } = 10;

    /// <summary>
    ///     Market history is read in pages of this many events, at most <see cref="HistoryMaximumPages" /> back, and
    ///     <see cref="HistoryOverlap" /> already stored events must be read again unchanged (Profit Fox Designer ADR 0087).
    /// </summary>
    public int HistoryPageSize { get; init; } = 100;

    public int HistoryMaximumPages { get; init; } = 10;

    public int HistoryOverlap { get; init; } = 10;

    /// <summary>
    ///     How often the order worker moves each account with open orders along: slots, unlocks, confirmations and fills.
    /// </summary>
    public TimeSpan OrderWorkerInterval { get; init; } = TimeSpan.FromSeconds(15);

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
            SoftResumeWindow > TimeSpan.Zero && CandleCloseDelay >= TimeSpan.Zero && HistoryReuse >= TimeSpan.Zero && WalletReuse >= TimeSpan.Zero &&
            DefaultAppId > 0 && MarkPriceInterval > TimeSpan.Zero && InventoryPageSize is > 0 and <= 5000 && InventoryMaximumPages > 0 &&
            DefiniteRejectionCodes.All(code => code > 1) &&
            ListingPagesMaximum is > 0 and <= 100 && HistoryPageSize is > 0 and <= 100 && HistoryMaximumPages is > 0 and <= 100 &&
            HistoryOverlap > 0 && HistoryOverlap < HistoryPageSize && OrderWorkerInterval >= TimeSpan.FromSeconds(1);

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