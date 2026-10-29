namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     Section <c>Venues:Bybit</c>: the REST origin, the streams and how failed public requests are retried. Signed
///     requests and the private stream use a bot's credential, decrypted only in memory (ADR 0017).
/// </summary>
public sealed class BybitOptions
{
    public const string SectionName = "Venues:Bybit";

    public string BaseAddress { get; init; } = "https://api.bybit.com/";

    /// <summary>
    ///     The public Spot WebSocket of live klines; it is never authenticated.
    /// </summary>
    public string PublicSpotStreamAddress { get; init; } = "wss://stream.bybit.com/v5/public/spot";

    /// <summary>
    ///     The private WebSocket of a live bot's orders and executions.
    /// </summary>
    public string PrivateStreamAddress { get; init; } = "wss://stream.bybit.com/v5/private";

    /// <summary>
    ///     How much longer than its timeframe a live feed may stay silent before it counts as stalled.
    /// </summary>
    public TimeSpan LiveFeedStallMargin { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxRetryAttempts { get; init; } = 3;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The budget for one request including retries and waits for Bybit's rate-limit reset.
    /// </summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public double CircuitFailureRatio { get; init; } = 0.5;
    public int CircuitMinimumThroughput { get; init; } = 10;
    public TimeSpan CircuitSamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CircuitBreakDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How long the list of tradable pairs is reused before it is read again.
    /// </summary>
    public TimeSpan InstrumentListLifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     The pause before a lost public candle stream is opened again.
    /// </summary>
    public TimeSpan LiveFeedReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The WebSocket keep-alive of the public and private streams.
    /// </summary>
    public TimeSpan StreamKeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     The largest stream message that is read; a larger one ends the connection.
    /// </summary>
    public int MaxStreamMessageBytes { get; init; } = 1_048_576;

    /// <summary>
    ///     How many resting Spot orders one bot may keep on a pair, which bounds a grid's buys plus its levels' sales (ADR 0058).
    /// </summary>
    public int MaxRestingOrdersPerBot { get; init; } = 200;

    /// <summary>
    ///     How long a measured offset to Bybit's server time is reused.
    /// </summary>
    public TimeSpan ServerClockLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The longest server-time round trip whose answer is trusted.
    /// </summary>
    public TimeSpan ServerClockMaxRoundTrip { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long the private stream's authentication signature stays valid.
    /// </summary>
    public TimeSpan PrivateStreamAuthLifetime { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How long the private stream may take to confirm the authentication and the subscription.
    /// </summary>
    public TimeSpan PrivateStreamConfirmTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How often the private stream sends Bybit's application-level ping.
    /// </summary>
    public TimeSpan PrivateStreamPingInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     How long closing the private stream may take.
    /// </summary>
    public TimeSpan PrivateStreamCloseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How old a place request must be before the order may be proven absent. Bybit's history lag is an open owner check
    ///     of ADR 0017; a day is far beyond any lag seen.
    /// </summary>
    public TimeSpan AbsenceSettleDelay { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     The oldest place request an absence proof accepts: the execution list covers seven days at most.
    /// </summary>
    public TimeSpan AbsenceMaxAge { get; init; } = TimeSpan.FromDays(6);

    /// <summary>
    ///     How far before the place request the executions are read, for clock differences.
    /// </summary>
    public TimeSpan AbsenceClockSkew { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The quote asset of the market scanner's universe: only pairs quoted in it are scanned (ADR 0059).
    /// </summary>
    public string ScannerQuoteAsset { get; init; } = "USDT";

    /// <summary>
    ///     The market scanner's REST budget. Bybit counts every request of the server's IP, the bots' orders included, so the
    ///     scanner stays far below the limit (ADR 0059).
    /// </summary>
    public int ScannerRequestsPerSecond { get; init; } = 10;

    /// <summary>
    ///     How long the market scanner sends no REST request after Bybit answered 403, which bans the IP for at least 10 minutes.
    /// </summary>
    public TimeSpan ScannerBanPause { get; init; } = TimeSpan.FromMinutes(10);

    public bool IsValid()
    {
        return IsValidBaseAddress(BaseAddress) &&
            Uri.TryCreate(PublicSpotStreamAddress, UriKind.Absolute, out Uri? stream) && stream.Scheme is "wss" or "ws" &&
            Uri.TryCreate(PrivateStreamAddress, UriKind.Absolute, out Uri? privateStream) && privateStream.Scheme is "wss" or "ws" &&
            LiveFeedStallMargin > TimeSpan.Zero &&
            MaxRetryAttempts is > 0 and <= 10 &&
            RetryBaseDelay > TimeSpan.Zero && RetryBaseDelay <= TotalTimeout &&
            AttemptTimeout > TimeSpan.Zero && TotalTimeout >= AttemptTimeout && TotalTimeout <= TimeSpan.FromDays(1) &&
            CircuitFailureRatio is > 0 and <= 1 && CircuitMinimumThroughput >= 2 &&
            CircuitSamplingDuration >= TimeSpan.FromMilliseconds(500) && CircuitBreakDuration >= TimeSpan.FromMilliseconds(500) &&
            InstrumentListLifetime >= TimeSpan.FromSeconds(1) && InstrumentListLifetime <= TimeSpan.FromDays(1) &&
            LiveFeedReconnectDelay > TimeSpan.Zero && StreamKeepAliveInterval > TimeSpan.Zero && MaxStreamMessageBytes > 0 &&
            ServerClockLifetime > TimeSpan.Zero && ServerClockMaxRoundTrip > TimeSpan.Zero &&
            PrivateStreamAuthLifetime > TimeSpan.Zero && PrivateStreamConfirmTimeout > TimeSpan.Zero && PrivateStreamPingInterval > TimeSpan.Zero &&
            PrivateStreamCloseTimeout > TimeSpan.Zero &&
            AbsenceSettleDelay > TimeSpan.Zero && AbsenceMaxAge > AbsenceSettleDelay && AbsenceMaxAge < TimeSpan.FromDays(7) && AbsenceClockSkew >= TimeSpan.Zero &&
            !string.IsNullOrWhiteSpace(ScannerQuoteAsset) && ScannerRequestsPerSecond is > 0 and <= 100 && ScannerBanPause >= TimeSpan.FromMinutes(10);
    }

    /// <summary>
    ///     An HTTP(S) origin without credentials, path, query or fragment.
    /// </summary>
    private static bool IsValidBaseAddress(string? address)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is "http" or "https" &&
            uri.AbsolutePath == "/" &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment);
    }
}