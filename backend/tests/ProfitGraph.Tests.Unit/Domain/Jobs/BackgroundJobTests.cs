using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;

namespace ProfitGraph.Tests.Unit.Domain.Jobs;

public sealed class BackgroundJobTests
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(year: 2026, month: 10, day: 24, hour: 10, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly BackgroundJobKind Kind = new BackgroundJobKind("history_download");

    [Test]
    public void QueuedJobHasNoProgressYet()
    {
        var owner = UserId.New();
        BackgroundJob job = Queued(owner);

        job.ShouldSatisfyAllConditions(
            () => job.Status.ShouldBe(BackgroundJobStatus.Queued),
            () => job.Progress.ShouldBe(BackgroundJobProgress.None),
            () => job.CancelRequested.ShouldBeFalse(),
            () => job.IsOwnedBy(owner).ShouldBeTrue(),
            () => job.IsOwnedBy(UserId.New()).ShouldBeFalse(),
            () => job.CreatedAt.ShouldBe(Created),
            () => job.StartedAt.ShouldBeNull());
    }

    [Test]
    public void RunningJobReportsMonotonicProgressAndCompletes()
    {
        BackgroundJob job = Running();

        job.ReportProgress(completed: 2, total: 10, Created.AddMinutes(1));
        job.ReportProgress(completed: 10, total: 10, Created.AddMinutes(2));
        job.Complete(Created.AddMinutes(3));

        job.Status.ShouldBe(BackgroundJobStatus.Completed);
        job.Progress.ShouldBe(new BackgroundJobProgress(completed: 10, total: 10));
        job.FinishedAt.ShouldBe(Created.AddMinutes(3));
        job.IsFinished.ShouldBeTrue();
    }

    [Test]
    public void ProgressCannotMoveBackwardsWithinAStageButANewStageRestartsIt()
    {
        BackgroundJob job = Running();
        job.ReportProgress(completed: 5, total: 10, Created);

        Should.Throw<BackgroundJobRuleException>(() => job.ReportProgress(completed: 4, total: 10, Created)).Code.ShouldBe(BackgroundJobRuleException.InvalidProgress);
        job.RestartProgress(completed: 0, total: 3, Created);

        job.Progress.ShouldBe(new BackgroundJobProgress(completed: 0, total: 3));
    }

    [TestCase(arg1: -1, arg2: 10)]
    [TestCase(arg1: 11, arg2: 10)]
    [TestCase(arg1: 0, arg2: -1)]
    public void ProgressStaysWithinItsTotal(long completed, long total)
    {
        Should.Throw<BackgroundJobRuleException>(() => new BackgroundJobProgress(completed, total)).Code.ShouldBe(BackgroundJobRuleException.InvalidProgress);
    }

    [Test]
    public void OnlyARunningJobReportsProgressOrCompletes()
    {
        BackgroundJob job = Queued();

        Should.Throw<BackgroundJobRuleException>(() => job.ReportProgress(completed: 1, total: 2, Created)).Code.ShouldBe(BackgroundJobRuleException.InvalidTransition);
        Should.Throw<BackgroundJobRuleException>(() => job.Complete(Created)).Code.ShouldBe(BackgroundJobRuleException.InvalidTransition);
    }

    [Test]
    public void QueuedJobIsCancelledAtOnce()
    {
        BackgroundJob job = Queued();

        job.RequestCancel(Created.AddMinutes(1));

        job.Status.ShouldBe(BackgroundJobStatus.Cancelled);
        job.FinishedAt.ShouldBe(Created.AddMinutes(1));
    }

    [Test]
    public void RunningJobTakesOneCancellationRequestAndIsCancelledWhenTheWorkStops()
    {
        BackgroundJob job = Running();

        job.RequestCancel(Created.AddMinutes(1));

        job.Status.ShouldBe(BackgroundJobStatus.Running);
        job.CancelRequested.ShouldBeTrue();
        Should.Throw<BackgroundJobRuleException>(() => job.RequestCancel(Created)).Code.ShouldBe(BackgroundJobRuleException.CancelAlreadyRequested);
        job.ConfirmCancelled(Created.AddMinutes(2));
        job.Status.ShouldBe(BackgroundJobStatus.Cancelled);
    }

    [Test]
    public void RunningJobIsNotCancelledWithoutARequest()
    {
        Should.Throw<BackgroundJobRuleException>(() => Running().ConfirmCancelled(Created)).Code.ShouldBe(BackgroundJobRuleException.InvalidTransition);
    }

    [Test]
    public void QueuedOrRunningJobCanFail()
    {
        BackgroundJob queued = Queued();
        BackgroundJob running = Running();

        queued.Fail(Created);
        running.Fail(Created);

        queued.Status.ShouldBe(BackgroundJobStatus.Failed);
        running.Status.ShouldBe(BackgroundJobStatus.Failed);
    }

    [Test]
    public void FinishedJobDoesNotChange()
    {
        BackgroundJob job = Running();
        job.Complete(Created);

        Should.Throw<BackgroundJobRuleException>(() => job.Fail(Created)).Code.ShouldBe(BackgroundJobRuleException.Finished);
        Should.Throw<BackgroundJobRuleException>(() => job.RequestCancel(Created)).Code.ShouldBe(BackgroundJobRuleException.Finished);
        Should.Throw<BackgroundJobRuleException>(() => job.Start(Created)).Code.ShouldBe(BackgroundJobRuleException.InvalidTransition);
    }

    [Test]
    public void JobStartsOnlyFromTheQueue()
    {
        BackgroundJob job = Running();

        Should.Throw<BackgroundJobRuleException>(() => job.Start(Created)).Code.ShouldBe(BackgroundJobRuleException.InvalidTransition);
        job.StartedAt.ShouldBe(Created);
    }

    [TestCase("history_download")]
    [TestCase("test_job2")]
    public void KindIsAStableName(string value)
    {
        new BackgroundJobKind(value).Value.ShouldBe(value);
    }

    [TestCase("")]
    [TestCase("HistoryDownload")]
    [TestCase("history-download")]
    public void KindRejectsOtherText(string value)
    {
        Should.Throw<ArgumentException>(() => new BackgroundJobKind(value));
    }

    [Test]
    public void IdentifierIsNeverEmpty()
    {
        Should.Throw<ArgumentException>(() => new BackgroundJobId(Guid.Empty));
        BackgroundJobId.New().ShouldNotBe(BackgroundJobId.New());
    }

    [Test]
    public void QueuedJobPausesAtOnceAndResumesToTheQueue()
    {
        BackgroundJob job = Queued();

        job.RequestPause(Created.AddMinutes(1));
        job.Status.ShouldBe(BackgroundJobStatus.Paused);
        job.IsFinished.ShouldBeFalse();
        Rule(() => job.RequestPause(Created.AddMinutes(2)), BackgroundJobRuleException.PauseAlreadyRequested);
        job.Resume(Created.AddMinutes(3));

        job.Status.ShouldBe(BackgroundJobStatus.Queued);
        Rule(() => job.Resume(Created.AddMinutes(4)), BackgroundJobRuleException.NotPaused);
    }

    [Test]
    public void RunningJobPausesOnlyAfterItsWorkStops()
    {
        BackgroundJob job = Running();
        Rule(() => job.ConfirmPaused(Created.AddMinutes(1)), BackgroundJobRuleException.InvalidTransition);

        job.RequestPause(Created.AddMinutes(1));
        job.Status.ShouldBe(BackgroundJobStatus.Running);
        job.PauseRequested.ShouldBeTrue();
        Rule(() => job.RequestPause(Created.AddMinutes(2)), BackgroundJobRuleException.PauseAlreadyRequested);
        job.ConfirmPaused(Created.AddMinutes(3));

        job.Status.ShouldBe(BackgroundJobStatus.Paused);
        job.PauseRequested.ShouldBeFalse();
    }

    [Test]
    public void APausedJobIsCancelledAtOnceAndACancellingJobIsNotPaused()
    {
        BackgroundJob paused = Queued();
        paused.RequestPause(Created.AddMinutes(1));
        paused.RequestCancel(Created.AddMinutes(2));
        paused.Status.ShouldBe(BackgroundJobStatus.Cancelled);

        BackgroundJob cancelling = Running();
        cancelling.RequestCancel(Created.AddMinutes(1));
        Rule(() => cancelling.RequestPause(Created.AddMinutes(2)), BackgroundJobRuleException.PauseAlreadyRequested);
    }

    [Test]
    public void InterruptedResumableJobIsQueuedUntilItStallsThreeTimesInARow()
    {
        BackgroundJob job = Running();
        job.RecordStoredWork(Created.AddMinutes(1));
        job.Interrupt(limit: 3, Created.AddMinutes(2)).ShouldBeTrue();
        job.Interruptions.ShouldBe(0);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            job.Start(Created.AddMinutes(2 + attempt));
            job.Advanced.ShouldBeFalse();
            job.Interrupt(limit: 3, Created.AddMinutes(2 + attempt)).ShouldBeTrue();
            job.Interruptions.ShouldBe(attempt);
            job.Status.ShouldBe(BackgroundJobStatus.Queued);
        }

        job.Start(Created.AddMinutes(6));
        job.Interrupt(limit: 3, Created.AddMinutes(7)).ShouldBeFalse();

        job.Status.ShouldBe(BackgroundJobStatus.Failed);
        job.FinishedAt.ShouldBe(Created.AddMinutes(7));
    }

    [Test]
    public void StoredWorkInAnAttemptRestartsTheStallCount()
    {
        BackgroundJob job = Running();
        job.Interrupt(limit: 3, Created.AddMinutes(1));
        job.Start(Created.AddMinutes(2));
        job.Interrupt(limit: 3, Created.AddMinutes(3));
        job.Interruptions.ShouldBe(2);
        job.Start(Created.AddMinutes(4));
        job.ReportProgress(completed: 1, total: 10, Created.AddMinutes(5));
        job.Advanced.ShouldBeFalse("Progress alone is not stored work.");
        job.RecordStoredWork(Created.AddMinutes(5));

        job.Interrupt(limit: 3, Created.AddMinutes(6)).ShouldBeTrue();

        job.Interruptions.ShouldBe(0);
    }

    private static void Rule(Action action, string code)
    {
        Should.Throw<BackgroundJobRuleException>(action).Code.ShouldBe(code);
    }

    private static BackgroundJob Queued(UserId? owner = null)
    {
        return BackgroundJob.Queue(BackgroundJobId.New(), Kind, owner ?? UserId.New(), Created);
    }

    private static BackgroundJob Running()
    {
        BackgroundJob job = Queued();
        job.Start(Created);

        return job;
    }
}