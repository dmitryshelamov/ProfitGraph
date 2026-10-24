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

    public bool IsValid()
    {
        return PollingInterval > TimeSpan.Zero && RenewInterval > TimeSpan.Zero && LeaseDuration > RenewInterval * 2;
    }
}