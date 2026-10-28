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

    /// <summary>
    ///     How many failed deliveries park a message: it is then no longer tried and readiness reports Degraded (ADR 0045).
    /// </summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>
    ///     How long a delivered message is kept before it is deleted.
    /// </summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(14);

    /// <summary>
    ///     How many delivered messages one retention transaction deletes at most.
    /// </summary>
    public int RetentionBatchSize { get; init; } = 1000;

    /// <summary>
    ///     How often delivered messages past <see cref="Retention" /> are deleted.
    /// </summary>
    public TimeSpan RetentionInterval { get; init; } = TimeSpan.FromHours(1);

    public bool IsValid()
    {
        return PollingInterval > TimeSpan.Zero && MaxRetryDelay > TimeSpan.Zero && MaxAttempts > 0 && Retention > TimeSpan.Zero && RetentionBatchSize > 0 &&
            RetentionInterval > TimeSpan.Zero;
    }
}