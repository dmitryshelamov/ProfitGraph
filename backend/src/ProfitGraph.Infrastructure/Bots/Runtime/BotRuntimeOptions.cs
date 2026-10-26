namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     Section <c>Bots:Runtime</c>: leases last 30 s and are renewed every 10 s; the host looks for bots to run every 2 s
///     (Profit Fox Designer ADR 0054).
/// </summary>
internal sealed class BotRuntimeOptions
{
    internal const string SectionName = "Bots:Runtime";

    /// <summary>
    ///     The name the live role records its heartbeat under.
    /// </summary>
    internal const string HeartbeatName = "live";

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RenewInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How long the intent dispatcher waits when no intent was due.
    /// </summary>
    public TimeSpan DispatchInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The first wait before a reconciliation the venue could not answer is tried again; later waits are 3, 6 and 12
    ///     times as long (5, 15, 30 and 60 s by default).
    /// </summary>
    public TimeSpan ReconcileRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often a runner looks for its bot's pending manual order.
    /// </summary>
    public TimeSpan ManualOrderPollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How often a runner looks for a newer saved version of its bot's strategy, so a version is followed without waiting
    ///     for the next candle (ADR 0019).
    /// </summary>
    public TimeSpan GraphCheckInterval { get; init; } = TimeSpan.FromSeconds(2);

    public bool IsValid()
    {
        return PollingInterval > TimeSpan.Zero && ReconcileRetryDelay > TimeSpan.Zero && ManualOrderPollInterval > TimeSpan.Zero && GraphCheckInterval > TimeSpan.Zero &&
            DispatchInterval > TimeSpan.Zero &&
            RenewInterval > TimeSpan.Zero &&
            LeaseDuration > RenewInterval * 2;
    }
}