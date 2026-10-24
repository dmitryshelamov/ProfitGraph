namespace ProfitGraph.Domain.Jobs;

public enum BackgroundJobStatus
{
    Queued,
    Running,

    /// <summary>
    ///     Stopped on request with its stored work kept; resuming queues it again (ADR 0011, stage 4.5).
    /// </summary>
    Paused,

    Completed,
    Failed,
    Cancelled
}