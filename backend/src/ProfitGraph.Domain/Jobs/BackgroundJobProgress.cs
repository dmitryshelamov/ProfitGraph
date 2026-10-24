namespace ProfitGraph.Domain.Jobs;

/// <summary>
///     How much of the current stage is done: 0 ≤ completed ≤ total. The total is unknown until the work reports it.
/// </summary>
public readonly record struct BackgroundJobProgress
{
    public static readonly BackgroundJobProgress None = new BackgroundJobProgress(completed: 0, total: null);

    public long Completed { get; }
    public long? Total { get; }

    public BackgroundJobProgress(long completed, long? total)
    {
        if (completed < 0 || total < 0 || completed > total)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.InvalidProgress, "Job progress must satisfy 0 <= completed <= total.");
        }

        Completed = completed;
        Total = total;
    }
}