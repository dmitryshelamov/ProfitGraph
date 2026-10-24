using NSubstitute;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.CancelBackgroundJob;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Tests.Unit.Common;

namespace ProfitGraph.Tests.Unit.Application.Jobs;

public sealed class CancelBackgroundJobCommandHandlerTests
{
    private static readonly FixedClock Clock = new FixedClock(new DateTimeOffset(year: 2026, month: 10, day: 24, hour: 12, minute: 0, second: 0, TimeSpan.Zero));
    private static readonly UserId Owner = UserId.New();
    private IBackgroundJobRepository _repository = null!;

    [SetUp]
    public void CreateRepository()
    {
        _repository = Substitute.For<IBackgroundJobRepository>();
    }

    [Test]
    public async Task QueuedJobIsCancelledFromTheQueue()
    {
        BackgroundJob job = Stored(BackgroundJobStatus.Queued);
        _repository.Update(job, BackgroundJobStatus.Queued, result: null, failure: null, Arg.Any<CancellationToken>()).Returns(true);

        BackgroundJobInternal result = await Handler().Handle(new CancelBackgroundJobCommand(Owner.Value, job.Id.Value), CancellationToken.None);

        result.Status.ShouldBe(BackgroundJobStatus.Cancelled);
    }

    [Test]
    public async Task WhenTheWorkerClaimsTheJobFirstTheRequestGoesToTheRunningJob()
    {
        BackgroundJob queued = Stored(BackgroundJobStatus.Queued);
        BackgroundJob running = Stored(BackgroundJobStatus.Running, queued.Id);
        _repository.Get(queued.Id, Arg.Any<CancellationToken>()).Returns(queued, running);
        _repository.Update(Arg.Any<BackgroundJob>(), BackgroundJobStatus.Queued, result: null, failure: null, Arg.Any<CancellationToken>()).Returns(false);
        _repository.Update(Arg.Any<BackgroundJob>(), BackgroundJobStatus.Running, result: null, failure: null, Arg.Any<CancellationToken>()).Returns(true);

        BackgroundJobInternal result = await Handler().Handle(new CancelBackgroundJobCommand(Owner.Value, queued.Id.Value), CancellationToken.None);

        result.Status.ShouldBe(BackgroundJobStatus.Running);
        result.CancelRequested.ShouldBeTrue();
    }

    [Test]
    public async Task FinishedJobOrRepeatedRequestIsAConflict()
    {
        BackgroundJob finished = Stored(BackgroundJobStatus.Completed);
        BackgroundJob requested = Stored(BackgroundJobStatus.Running, cancelRequested: true);

        (await Should.ThrowAsync<StateConflictException>(() => Handler().Handle(new CancelBackgroundJobCommand(Owner.Value, finished.Id.Value), CancellationToken.None)))
            .Code.ShouldBe(BackgroundJobRuleException.Finished);
        (await Should.ThrowAsync<StateConflictException>(() => Handler().Handle(new CancelBackgroundJobCommand(Owner.Value, requested.Id.Value), CancellationToken.None)))
            .Code.ShouldBe(BackgroundJobRuleException.CancelAlreadyRequested);
    }

    [Test]
    public async Task AnotherUsersJobIsReportedAsMissing()
    {
        BackgroundJob job = Stored(BackgroundJobStatus.Queued);

        var error = await Should.ThrowAsync<ResourceNotFoundException>(() =>
            Handler().Handle(new CancelBackgroundJobCommand(Guid.NewGuid(), job.Id.Value), CancellationToken.None));

        error.Code.ShouldBe(BackgroundJobErrorCodes.NotFound);
        await _repository.DidNotReceiveWithAnyArgs().Update(null!, BackgroundJobStatus.Queued, result: null, failure: null, CancellationToken.None);
    }

    [Test]
    public async Task QueueStoresTheKindOfTheRequestType()
    {
        BackgroundJobInternal job = await new BackgroundJobQueue(_repository, Clock).Enqueue(Owner, new TestWorkRequest(Size: 3), CancellationToken.None);

        job.Kind.ShouldBe("test_work");
        job.Status.ShouldBe(BackgroundJobStatus.Queued);
        await _repository.Received(1)
            .Add(
                Arg.Is<BackgroundJob>(stored => stored != null && stored.OwnerId == Owner && stored.Kind == TestWorkRequest.Kind),
                new TestWorkRequest(Size: 3),
                Arg.Any<CancellationToken>());
    }

    private CancelBackgroundJobCommandHandler Handler()
    {
        return new CancelBackgroundJobCommandHandler(_repository, Clock);
    }

    private BackgroundJob Stored(BackgroundJobStatus status, BackgroundJobId? id = null, bool cancelRequested = false)
    {
        BackgroundJobId jobId = id ?? BackgroundJobId.New();
        bool started = status != BackgroundJobStatus.Queued;
        BackgroundJob job = BackgroundJob.Restore(
            jobId,
            TestWorkRequest.Kind,
            Owner,
            status,
            BackgroundJobProgress.None,
            cancelRequested,
            Clock.GetUtcNow(),
            started ? Clock.GetUtcNow() : null,
            status is BackgroundJobStatus.Completed ? Clock.GetUtcNow() : null,
            Clock.GetUtcNow());

        if (id is null)
        {
            _repository.Get(jobId, Arg.Any<CancellationToken>()).Returns(job);
        }

        return job;
    }
}