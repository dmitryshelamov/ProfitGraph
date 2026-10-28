namespace ProfitGraph.Infrastructure.Jobs;

internal sealed class BackgroundJobOptions
{
    internal const string SectionName = "BackgroundJobs";

    /// <summary>
    ///     The name the Worker records its heartbeat under.
    /// </summary>
    internal const string WorkerName = "worker";

    /// <summary>
    ///     How often the Worker looks for queued jobs, checks cancellation of the running job and beats its heartbeat.
    /// </summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Consecutive interruptions without progress after which a resumable job fails instead of being queued again.
    /// </summary>
    public int InterruptionLimit { get; init; } = 3;

    public bool IsValid()
    {
        return PollingInterval > TimeSpan.Zero && InterruptionLimit > 0;
    }
}