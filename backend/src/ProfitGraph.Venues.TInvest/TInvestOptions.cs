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
    ///     The pause before an order state is read again after its executed lots and trade stages disagreed.
    /// </summary>
    public TimeSpan OrderReadRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The directory with the pinned Russian Trusted Root and Sub CA files, relative to the application directory. It
    ///     moves the files, never the trust: their fingerprints are pinned in code (Profit Fox Designer ADR 0085).
    /// </summary>
    public string TrustedRootsPath { get; init; } = "Certificates/TInvest";

    public TInvestRequestLimits Limits { get; init; } = new TInvestRequestLimits();

    public TInvestHistoryOptions History { get; init; } = new TInvestHistoryOptions();

    /// <summary>
    ///     How long after a candle closes the live feed waits before it reads the interval, so late trades are in.
    /// </summary>
    public TimeSpan CandleCloseGrace { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     The least order value used to derive the account's commission rate from <c>GetOrderPrice</c>, so rounding to the
    ///     kopeck does not distort it.
    /// </summary>
    public decimal CommissionProbeNotional { get; init; } = 100_000m;

    /// <summary>
    ///     How far above the last price a market buy sizes its lots, so its quote budget covers the fill (ADR 0023).
    /// </summary>
    public decimal MarketBuySlippage { get; init; } = 0.01m;

    /// <summary>
    ///     How often the trading status and the price band of locally waiting orders are read again (ADR 0023).
    /// </summary>
    public TimeSpan WaitingRecheckInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long before the end of the trading day's last exchange interval working DAY orders are cancelled to carry their
    ///     remainder to the next trading date (ADR 0023). Positive and at most an hour.
    /// </summary>
    public TimeSpan CarryOverLead { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     How often the state of a final order whose commission is still zero is read again for its late commission.
    /// </summary>
    public TimeSpan CommissionRecheckInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     How long after it became final an order's late commission is still looked for. A commission that comes later stays
    ///     pending at the bot's rate.
    /// </summary>
    public TimeSpan CommissionPendingWindow { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    ///     How long after the end of a trading day its working orders must have been gone before the module may treat a
    ///     missing one as expired and without execution (ADR 0030): the broker's operations lag behind the exchange.
    /// </summary>
    public TimeSpan AbsenceSettleDelay { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     The longest life of an order for which the module reads the account's operations to prove its absence. An older order
    ///     stays with the owner.
    /// </summary>
    public TimeSpan AbsenceMaxWindow { get; init; } = TimeSpan.FromDays(10);

    /// <summary>
    ///     How much earlier than the order was planned an operation of the account may still be dated and be that order's execution
    ///     (ADR 0034): the margin for the difference between this process's clock and T-Invest's. An operation dated earlier cannot
    ///     be an execution of the order, because an order cannot execute before it exists.
    /// </summary>
    public TimeSpan AbsenceClockSkew { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     How often, after a trading day ended and until the next session opens, the state of DAY orders no runner reads is
    ///     read again and recorded (ADR 0031). Positive and at most an hour.
    /// </summary>
    public TimeSpan ClosedSessionSweepInterval { get; init; } = TimeSpan.FromMinutes(5);

    internal bool IsValid()
    {
        if (RequestTimeout <= TimeSpan.Zero || CandleCloseGrace < TimeSpan.Zero || CommissionProbeNotional <= 0m || MarketBuySlippage is < 0m or >= 0.2m ||
            WaitingRecheckInterval <= TimeSpan.Zero || CarryOverLead <= TimeSpan.Zero || CarryOverLead > TimeSpan.FromHours(1) ||
            CommissionRecheckInterval <= TimeSpan.Zero ||
            CommissionPendingWindow <= TimeSpan.Zero ||
            AbsenceSettleDelay < TimeSpan.Zero || AbsenceSettleDelay > TimeSpan.FromDays(1) ||
            AbsenceMaxWindow <= TimeSpan.Zero || AbsenceMaxWindow > TimeSpan.FromDays(30) ||
            AbsenceClockSkew < TimeSpan.Zero || AbsenceClockSkew > TimeSpan.FromMinutes(5) ||
            ClosedSessionSweepInterval <= TimeSpan.Zero || ClosedSessionSweepInterval > TimeSpan.FromHours(1) ||
            !Limits.IsValid() || !History.IsValid() ||
            string.IsNullOrWhiteSpace(AppName) ||
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