namespace ProfitGraph.Venues.TInvest;

/// <summary>
///     Section <c>Venues:TInvest:Scanner</c> (ADR 0062): the stored T-Invest credential the market scanner reads with, and the
///     scanner's share of that token's broker limits. T-Invest has a scanner only while the venue is enabled and
///     <see cref="CredentialId" /> names a credential.
/// </summary>
public sealed class TInvestScannerOptions
{
    /// <summary>
    ///     A stored T-Invest credential of the owner, ideally with a read-only token of its own, so the scanner never spends the
    ///     limits of a trading token.
    /// </summary>
    public Guid? CredentialId { get; init; }

    /// <summary>
    ///     How many market data requests a minute the scanner starts at most, one after another: a share of the token's
    ///     <c>MarketData</c> limit that leaves room for charts and bots.
    /// </summary>
    public int RequestsPerMinute { get; init; } = 120;

    /// <summary>
    ///     How many candle subscriptions a minute the scanner's streams ask for at most, below the broker's 100 subscription changes
    ///     a minute per token.
    /// </summary>
    public int SubscriptionsPerMinute { get; init; } = 50;

    /// <summary>
    ///     How many shares one market data stream carries; the broker allows 300 subscriptions per stream and 32 streams per token.
    /// </summary>
    public int SharesPerStream { get; init; } = 50;

    /// <summary>
    ///     How often the broker pings an idle stream, so a closed market does not look like a lost connection.
    /// </summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long the decrypted token is reused before the credential is read again.
    /// </summary>
    public TimeSpan CredentialReuse { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Whether shares only qualified investors may buy are scanned.
    /// </summary>
    public bool QualifiedInvestor { get; init; }

    internal bool IsEnabled => CredentialId is not null;

    internal bool IsValid()
    {
        return RequestsPerMinute is > 0 and <= 600 && SubscriptionsPerMinute is > 0 and <= 100 && SharesPerStream > 0 &&
            SharesPerStream <= Math.Min(SubscriptionsPerMinute, val2: 300) && PingInterval >= TimeSpan.FromSeconds(5) &&
            PingInterval <= TimeSpan.FromMinutes(1) && CredentialReuse >= TimeSpan.Zero;
    }
}