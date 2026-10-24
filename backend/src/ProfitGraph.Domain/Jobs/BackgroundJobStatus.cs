namespace ProfitGraph.Domain.Jobs;

public enum BackgroundJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}