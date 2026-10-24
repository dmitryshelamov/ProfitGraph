using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Jobs;

/// <summary>
///     A durable unit of background work (ADR 0011): lifecycle and progress only, never the work's parameters or result.
///     Queued → Running → Completed | Failed | Cancelled, and Queued → Cancelled. A running job is cancelled by request:
///     the runner sees the request and records the outcome.
/// </summary>
public sealed class BackgroundJob : AggregateRoot
{
    public BackgroundJobId Id { get; }
    public BackgroundJobKind Kind { get; }
    public UserId OwnerId { get; }
    public BackgroundJobStatus Status { get; private set; }
    public BackgroundJobProgress Progress { get; private set; }
    public bool CancelRequested { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsFinished => Status is BackgroundJobStatus.Completed or BackgroundJobStatus.Failed or BackgroundJobStatus.Cancelled;

    private BackgroundJob(
        BackgroundJobId id,
        BackgroundJobKind kind,
        UserId ownerId,
        BackgroundJobStatus status,
        BackgroundJobProgress progress,
        bool cancelRequested,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? finishedAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Kind = kind;
        OwnerId = ownerId;
        Status = status;
        Progress = progress;
        CancelRequested = cancelRequested;
        CreatedAt = createdAt;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        UpdatedAt = updatedAt;
    }

    public static BackgroundJob Queue(BackgroundJobId id, BackgroundJobKind kind, UserId ownerId, DateTimeOffset now)
    {
        return new BackgroundJob(
            id,
            kind,
            ownerId,
            BackgroundJobStatus.Queued,
            BackgroundJobProgress.None,
            cancelRequested: false,
            now,
            startedAt: null,
            finishedAt: null,
            now);
    }

    public static BackgroundJob Restore(
        BackgroundJobId id,
        BackgroundJobKind kind,
        UserId ownerId,
        BackgroundJobStatus status,
        BackgroundJobProgress progress,
        bool cancelRequested,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? finishedAt,
        DateTimeOffset updatedAt)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException("Invalid stored job status.", nameof(status));
        }

        return new BackgroundJob(id, kind, ownerId, status, progress, cancelRequested, createdAt, startedAt, finishedAt, updatedAt);
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    public void Start(DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Queued);
        Status = BackgroundJobStatus.Running;
        StartedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Advances the current stage; progress within a stage never moves backwards.
    /// </summary>
    public void ReportProgress(long completed, long total, DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Running);
        var progress = new BackgroundJobProgress(completed, total);

        if (completed < Progress.Completed)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.InvalidProgress, "Job progress cannot move backwards within a stage.");
        }

        Progress = progress;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Starts counting the next stage of the work from <paramref name="completed" />.
    /// </summary>
    public void RestartProgress(long completed, long total, DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Running);
        Progress = new BackgroundJobProgress(completed, total);
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Running);
        Finish(BackgroundJobStatus.Completed, now);
    }

    public void Fail(DateTimeOffset now)
    {
        EnsureNotFinished();
        Finish(BackgroundJobStatus.Failed, now);
    }

    /// <summary>
    ///     Cancels a queued job at once; a running job only takes the request and is cancelled when its work stops.
    /// </summary>
    public void RequestCancel(DateTimeOffset now)
    {
        EnsureNotFinished();

        if (Status == BackgroundJobStatus.Queued)
        {
            Finish(BackgroundJobStatus.Cancelled, now);

            return;
        }

        if (CancelRequested)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.CancelAlreadyRequested, "Cancellation of this job is already requested.");
        }

        CancelRequested = true;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Records that running work stopped because cancellation was requested.
    /// </summary>
    public void ConfirmCancelled(DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Running);

        if (!CancelRequested)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.InvalidTransition, "A running job is cancelled only after a cancellation request.");
        }

        Finish(BackgroundJobStatus.Cancelled, now);
    }

    private void Finish(BackgroundJobStatus status, DateTimeOffset now)
    {
        Status = status;
        FinishedAt = now;
        UpdatedAt = now;
    }

    private void EnsureNotFinished()
    {
        if (IsFinished)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.Finished, $"The job is already {Status}.");
        }
    }

    private void EnsureStatus(BackgroundJobStatus expected)
    {
        if (Status != expected)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.InvalidTransition, $"The job must be {expected}, but is {Status}.");
        }
    }
}