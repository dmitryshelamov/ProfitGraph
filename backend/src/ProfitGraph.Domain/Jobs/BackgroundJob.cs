using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Jobs;

/// <summary>
///     A durable unit of background work (ADR 0011): lifecycle and progress only, never the work's parameters or result.
///     Queued → Running → Completed | Failed | Cancelled, and Queued → Cancelled. A running job is cancelled or paused by
///     request: the runner sees the request and records the outcome. A paused job keeps its stored work and is queued
///     again on resume; a resumable job interrupted by a Worker restart is queued again too.
/// </summary>
public sealed class BackgroundJob : AggregateRoot
{
    public BackgroundJobId Id { get; }
    public BackgroundJobKind Kind { get; }
    public UserId OwnerId { get; }
    public BackgroundJobStatus Status { get; private set; }
    public BackgroundJobProgress Progress { get; private set; }
    public bool CancelRequested { get; private set; }
    public bool PauseRequested { get; private set; }

    /// <summary>
    ///     Consecutive interruptions of a resumable job in which it stored no work.
    /// </summary>
    public int Interruptions { get; private set; }

    /// <summary>
    ///     Whether the current attempt stored work that a later attempt does not redo; a new attempt starts without.
    /// </summary>
    public bool Advanced { get; private set; }

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
        DateTimeOffset updatedAt,
        bool pauseRequested,
        int interruptions,
        bool advanced)
    {
        Id = id;
        PauseRequested = pauseRequested;
        Interruptions = interruptions;
        Advanced = advanced;
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
            now,
            pauseRequested: false,
            interruptions: 0,
            advanced: false);
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
        DateTimeOffset updatedAt,
        bool pauseRequested = false,
        int interruptions = 0,
        bool advanced = false)
    {
        if (!Enum.IsDefined(status) || interruptions < 0 || (pauseRequested && status != BackgroundJobStatus.Running))
        {
            throw new ArgumentException("Invalid stored job status, interruptions or pause request.", nameof(status));
        }

        return new BackgroundJob(
            id,
            kind,
            ownerId,
            status,
            progress,
            cancelRequested,
            createdAt,
            startedAt,
            finishedAt,
            updatedAt,
            pauseRequested,
            interruptions,
            advanced);
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    public void Start(DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Queued);
        Status = BackgroundJobStatus.Running;
        Advanced = false;

        // Each attempt counts its progress from the start; a resumed job restarts it from the work it stored.
        Progress = BackgroundJobProgress.None;
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

        if (Status is BackgroundJobStatus.Queued or BackgroundJobStatus.Paused)
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
    ///     Records that the current attempt stored work that a later attempt does not redo, which resets the count of
    ///     interruptions without progress.
    /// </summary>
    public void RecordStoredWork(DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Running);
        Advanced = true;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Pauses a queued job at once; a running job only takes the request and is paused when its work stops. A job with
    ///     a cancellation request is not paused.
    /// </summary>
    public void RequestPause(DateTimeOffset now)
    {
        EnsureNotFinished();

        if (Status == BackgroundJobStatus.Queued)
        {
            Status = BackgroundJobStatus.Paused;
            UpdatedAt = now;

            return;
        }

        if (Status == BackgroundJobStatus.Paused || PauseRequested || CancelRequested)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.PauseAlreadyRequested, "The job is already paused or stopping.");
        }

        PauseRequested = true;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Records that running work stopped because a pause was requested; the work it stored is kept.
    /// </summary>
    public void ConfirmPaused(DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Running);

        if (!PauseRequested)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.InvalidTransition, "A running job is paused only after a pause request.");
        }

        Status = BackgroundJobStatus.Paused;
        PauseRequested = false;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status != BackgroundJobStatus.Paused)
        {
            throw new BackgroundJobRuleException(BackgroundJobRuleException.NotPaused, $"Only a paused job is resumed; this one is {Status}.");
        }

        Status = BackgroundJobStatus.Queued;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Queues a resumable job that a Worker restart or shutdown interrupted. After <paramref name="limit" /> consecutive
    ///     interruptions in which it stored no work it fails instead; returns whether it was queued.
    /// </summary>
    public bool Interrupt(int limit, DateTimeOffset now)
    {
        EnsureStatus(BackgroundJobStatus.Running);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, other: 1);
        Interruptions = Advanced ? 0 : Interruptions + 1;
        PauseRequested = false;

        if (Interruptions >= limit)
        {
            Finish(BackgroundJobStatus.Failed, now);

            return false;
        }

        Status = BackgroundJobStatus.Queued;
        UpdatedAt = now;

        return true;
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