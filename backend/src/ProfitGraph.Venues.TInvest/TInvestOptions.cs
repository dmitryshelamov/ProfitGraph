namespace ProfitGraph.Venues.TInvest;

/// <summary>
///     Section <c>Venues:TInvest</c>. T-Invest stays off unless the owner turns it on: no request leaves the process while
///     <see cref="Enabled" /> is false, and the endpoint is empty until configured. It accepts only the official HTTPS host,
///     or an invented <c>.invalid</c> host in tests (ADR 0023). Every limit and duration is a local policy.
/// </summary>
public sealed class TInvestOptions
{
    public const string SectionName = "Venues:TInvest";

    public const string OfficialHost = "invest-public-api.tbank.ru";

    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    ///     The name ProfitGraph gives itself in the <c>x-app-name</c> header.
    /// </summary>
    public string AppName { get; init; } = "ProfitGraph";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The directory with the pinned Russian Trusted Root and Sub CA files, relative to the application directory. It
    ///     moves the files, never the trust: their fingerprints are pinned in code (Profit Fox Designer ADR 0085).
    /// </summary>
    public string TrustedRootsPath { get; init; } = "Certificates/TInvest";

    public TInvestRequestLimits Limits { get; init; } = new TInvestRequestLimits();

    public TInvestHistoryOptions History { get; init; } = new TInvestHistoryOptions();

    internal bool IsValid()
    {
        if (RequestTimeout <= TimeSpan.Zero || !Limits.IsValid() || !History.IsValid() || string.IsNullOrWhiteSpace(AppName) ||
            string.IsNullOrWhiteSpace(TrustedRootsPath))
        {
            return false;
        }

        if (!Enabled)
        {
            return true;
        }

        return Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? endpoint) &&
            endpoint.Scheme == Uri.UriSchemeHttps &&
            endpoint.AbsolutePath == "/" &&
            string.IsNullOrEmpty(endpoint.Query) &&
            (endpoint.Host == OfficialHost || endpoint.Host.EndsWith(".invalid", StringComparison.Ordinal));
    }
}