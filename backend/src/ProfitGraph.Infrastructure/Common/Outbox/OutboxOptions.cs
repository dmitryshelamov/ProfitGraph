namespace ProfitGraph.Infrastructure.Common.Outbox;

internal sealed class OutboxOptions
{
    internal const string SectionName = "Outbox";

    /// <summary>
    ///     How often the dispatcher looks for undelivered events when it found none.
    /// </summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The longest wait before a failed delivery is tried again.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    public bool IsValid()
    {
        return PollingInterval > TimeSpan.Zero && MaxRetryDelay > TimeSpan.Zero;
    }
}