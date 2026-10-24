namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     Section <c>Venues:Bybit</c>: the public REST origin and how failed requests are retried. Every request is public;
///     the module holds no credentials.
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

    public bool IsValid()
    {
        return IsValidBaseAddress(BaseAddress) &&
            Uri.TryCreate(PublicSpotStreamAddress, UriKind.Absolute, out Uri? stream) && stream.Scheme is "wss" or "ws" &&
            LiveFeedStallMargin > TimeSpan.Zero &&
            MaxRetryAttempts is > 0 and <= 10 &&
            RetryBaseDelay > TimeSpan.Zero && RetryBaseDelay <= TotalTimeout &&
            AttemptTimeout > TimeSpan.Zero && TotalTimeout >= AttemptTimeout && TotalTimeout <= TimeSpan.FromDays(1) &&
            CircuitFailureRatio is > 0 and <= 1 && CircuitMinimumThroughput >= 2 &&
            CircuitSamplingDuration >= TimeSpan.FromMilliseconds(500) && CircuitBreakDuration >= TimeSpan.FromMilliseconds(500) &&
            InstrumentListLifetime >= TimeSpan.FromSeconds(1) && InstrumentListLifetime <= TimeSpan.FromDays(1);
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