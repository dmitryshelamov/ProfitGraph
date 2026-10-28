using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     The runtime part of a bot's state on its own: which reports each status and reason accepts. The bot's desired state,
///     events and timestamps are covered by <see cref="BotTransitionsTests" />.
/// </summary>
public sealed class BotRuntimeStateTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    private static readonly BotRuntimeState Running = new BotRuntimeState(BotRuntimeStatus.Running, Reason: null, RestartAttempts: 0, NextRestartAt: null);

    [TestCase(BotRuntimeStatus.Starting, BotRuntimeStatus.Starting)]
    [TestCase(BotRuntimeStatus.Running, BotRuntimeStatus.Running)]
    [TestCase(BotRuntimeStatus.Stopped, BotRuntimeStatus.Created)]
    [TestCase(BotRuntimeStatus.Degraded, BotRuntimeStatus.Created)]
    [TestCase(BotRuntimeStatus.Created, BotRuntimeStatus.Created)]
    public void StartClearsTheReasonAndStreakAndKeepsALiveRuntime(BotRuntimeStatus status, BotRuntimeStatus expected)
    {
        var state = new BotRuntimeState(status, BotStatusReason.RuntimeFailure, RestartAttempts: 3, Now);

        state.Start().ShouldBe(new BotRuntimeState(expected, Reason: null, RestartAttempts: 0, NextRestartAt: null));
    }

    [Test]
    public void KillStopsWithItsControlReasonOnly()
    {
        Running.Kill(BotStatusReason.Killed).ShouldBe(Running with { Status = BotRuntimeStatus.Stopped, Reason = BotStatusReason.Killed });
        Should.Throw<ArgumentException>(() => Running.Kill(BotStatusReason.ForeignOrder));
    }

    [Test]
    public void ADegradedLeaseCountsARestartUpToTheCap()
    {
        BotRuntimeState failed = Running.RecordFailure(Now)!;

        failed.LeaseAcquired().RestartAttempts.ShouldBe(1);
        (failed with { RestartAttempts = BotRestartSchedule.MaxCountedAttempts }).LeaseAcquired().RestartAttempts.ShouldBe(BotRestartSchedule.MaxCountedAttempts);
        Running.LeaseAcquired().ShouldBe(Running);
    }

    [Test]
    public void OnlyARuntimeFailureWaitsForItsRestartTime()
    {
        BotRuntimeState failed = Running.RecordFailure(Now)!;

        Running.IsLeasable(Now).ShouldBeTrue();
        failed.IsLeasable(Now).ShouldBeFalse();
        failed.IsLeasable(failed.NextRestartAt!.Value).ShouldBeTrue();
        (failed with { NextRestartAt = null }).IsLeasable(Now).ShouldBeTrue();
        Running.Degrade(BotStatusReason.ForeignOrder)!.IsLeasable(Now.AddDays(1)).ShouldBeFalse();
    }

    [Test]
    public void ASafetyReasonRefusesStartingAndStoppedReports()
    {
        BotRuntimeState degraded = Running.Degrade(BotStatusReason.UnresolvedOrder)!;

        degraded.ShouldSatisfyAllConditions(
            () => degraded.HoldsSafetyReason.ShouldBeTrue(),
            () => degraded.MarkStarting().ShouldBeNull(),
            () => degraded.MarkRunning().ShouldBeNull(),
            () => degraded.MarkStopped().ShouldBeNull(),
            () => degraded.RecordFailure(Now).ShouldBeNull());
    }

    [Test]
    public void ARuntimeFailureRestartsThroughStarting()
    {
        BotRuntimeState failed = Running.RecordFailure(Now)!;

        failed.MarkRunning().ShouldBeNull();
        failed.MarkStarting().ShouldBe(failed with { Status = BotRuntimeStatus.Starting, Reason = null });
    }

    [Test]
    public void StoppedReportKeepsOnlyAControlReason()
    {
        BotRuntimeState failed = Running.RecordFailure(Now)!;

        Running.MarkStopped().ShouldBe(Running with { Status = BotRuntimeStatus.Stopped });
        failed.MarkStopped()!.Reason.ShouldBeNull();
        Running.Kill(BotStatusReason.DailyLossLimit).MarkStopped().ShouldBeNull();
        (Running with { Status = BotRuntimeStatus.Starting, Reason = BotStatusReason.Killed }).MarkStopped()!.Reason.ShouldBe(BotStatusReason.Killed);
    }

    [Test]
    public void AFailureAfterAKillKeepsTheKill()
    {
        Running.Kill(BotStatusReason.Killed).RecordFailure(Now).ShouldBeNull();
    }

    [Test]
    public void AFailureSchedulesTheNextRestart()
    {
        BotRuntimeState failed = Running.RecordFailure(Now)!;

        failed.ShouldBe(
            new BotRuntimeState(BotRuntimeStatus.Degraded, BotStatusReason.RuntimeFailure, RestartAttempts: 0, BotRestartSchedule.NextRestartAt(attempts: 0, Now)));
    }

    [Test]
    public void DegradeTakesANewSafetyReasonOnly()
    {
        BotRuntimeState degraded = Running.Degrade(BotStatusReason.InsufficientBalance)!;

        degraded.Degrade(BotStatusReason.InsufficientBalance).ShouldBeNull();
        degraded.Degrade(BotStatusReason.ForeignOrder)!.Reason.ShouldBe(BotStatusReason.ForeignOrder);
        Should.Throw<ArgumentException>(() => Running.Degrade(BotStatusReason.RuntimeFailure));
    }

    [Test]
    public void ClearReturnsOnlyItsOwnReasonToCreated()
    {
        BotRuntimeState degraded = Running.Degrade(BotStatusReason.UnresolvedOrder)! with { RestartAttempts = 2 };

        degraded.Clear(BotStatusReason.VenueAccountProtection, resetRestarts: false).ShouldBeNull();
        degraded.Clear(BotStatusReason.UnresolvedOrder, resetRestarts: false)
            .ShouldBe(new BotRuntimeState(BotRuntimeStatus.Created, Reason: null, RestartAttempts: 2, NextRestartAt: null));
        degraded.Clear(BotStatusReason.UnresolvedOrder, resetRestarts: true)!.RestartAttempts.ShouldBe(0);
        Running.Clear(BotStatusReason.UnresolvedOrder, resetRestarts: true).ShouldBeNull();
    }

    [Test]
    public void StartReArmsOnlyAFailedIntentOrBalance()
    {
        Running.Degrade(BotStatusReason.IntentFailed)!.IsRestartableByStart.ShouldBeTrue();
        Running.Degrade(BotStatusReason.InsufficientBalance)!.IsRestartableByStart.ShouldBeTrue();
        Running.Degrade(BotStatusReason.ForeignOrder)!.IsRestartableByStart.ShouldBeFalse();
        Running.RecordFailure(Now)!.IsRestartableByStart.ShouldBeFalse();
    }
}