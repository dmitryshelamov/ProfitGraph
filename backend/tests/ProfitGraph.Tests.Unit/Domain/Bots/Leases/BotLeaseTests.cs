using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;

namespace ProfitGraph.Tests.Unit.Domain.Bots.Leases;

/// <summary>
///     One runtime per bot (Profit Fox Designer ADR 0054): 30-second leases, takeover under a new epoch, fenced holders.
/// </summary>
public sealed class BotLeaseTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);
    private static readonly LeaseHolderId First = LeaseHolderId.New();
    private static readonly LeaseHolderId Second = LeaseHolderId.New();

    [Test]
    public void FirstLeaseStartsAtEpochOneWithAClosedGate()
    {
        BotLease lease = BotLease.Acquire(BotId.New(), First, Now, Duration);

        lease.ShouldSatisfyAllConditions(
            () => lease.Epoch.ShouldBe(1),
            () => lease.ExpiresAt.ShouldBe(Now + Duration),
            () => lease.SubmissionsReady.ShouldBeFalse(),
            () => lease.IsHeldBy(First, epoch: 1, Now).ShouldBeTrue());
    }

    [Test]
    public void ActiveLeaseCannotBeTakenOver()
    {
        BotLease lease = Ready();

        Should.Throw<BotRuleException>(() => lease.TakeOver(Second, Now.AddSeconds(29), Duration)).Code.ShouldBe(BotRuleException.LeaseHeld);
        lease.HolderId.ShouldBe(First);
    }

    [Test]
    public void ExpiredLeaseIsTakenOverUnderTheNextEpochWithAClosedGate()
    {
        BotLease lease = Ready();
        DateTimeOffset later = Now + Duration;

        lease.TakeOver(Second, later, Duration);

        lease.ShouldSatisfyAllConditions(
            () => lease.HolderId.ShouldBe(Second),
            () => lease.Epoch.ShouldBe(2),
            () => lease.SubmissionsReady.ShouldBeFalse(),
            () => lease.IsHeldBy(First, epoch: 1, later).ShouldBeFalse(),
            () => lease.IsHeldBy(Second, epoch: 2, later).ShouldBeTrue());
    }

    [Test]
    public void SameHolderTakingOverAlsoMovesTheEpoch()
    {
        BotLease lease = Ready();

        lease.TakeOver(First, Now + Duration, Duration);

        lease.Epoch.ShouldBe(2);
        lease.IsHeldBy(First, epoch: 1, Now + Duration).ShouldBeFalse();
    }

    [Test]
    public void HolderRenewsBeforeExpiry()
    {
        BotLease lease = Ready();

        lease.Renew(First, epoch: 1, Now.AddSeconds(10), Duration).ShouldBeTrue();

        lease.ExpiresAt.ShouldBe(Now.AddSeconds(40));
    }

    [Test]
    public void StaleHolderCannotRenew()
    {
        BotLease lease = Ready();

        lease.Renew(First, epoch: 1, Now + Duration, Duration).ShouldBeFalse();
        lease.Renew(Second, epoch: 1, Now.AddSeconds(10), Duration).ShouldBeFalse();
        lease.Renew(First, epoch: 2, Now.AddSeconds(10), Duration).ShouldBeFalse();
        lease.ExpiresAt.ShouldBe(Now + Duration);
    }

    [Test]
    public void ReleasedLeaseIsFreeAtOnce()
    {
        BotLease lease = Ready();

        lease.Release(First, epoch: 1, Now).ShouldBeTrue();

        lease.IsExpired(Now).ShouldBeTrue();
        lease.SubmissionsReady.ShouldBeFalse();
        Should.NotThrow(() => lease.TakeOver(Second, Now, Duration));
    }

    [Test]
    public void OnlyTheHolderReleases()
    {
        BotLease lease = Ready();

        lease.Release(Second, epoch: 1, Now).ShouldBeFalse();

        lease.IsHeldBy(First, epoch: 1, Now).ShouldBeTrue();
    }

    [Test]
    public void GateOpensOnlyForTheCurrentHolder()
    {
        BotLease lease = BotLease.Acquire(BotId.New(), First, Now, Duration);

        lease.OpenSubmitGate(Second, epoch: 1, Now).ShouldBeFalse();
        lease.OpenSubmitGate(First, epoch: 1, Now + Duration).ShouldBeFalse();
        lease.SubmissionsReady.ShouldBeFalse();
        lease.OpenSubmitGate(First, epoch: 1, Now).ShouldBeTrue();
        lease.SubmissionsReady.ShouldBeTrue();
    }

    [Test]
    public void GateCloses()
    {
        BotLease lease = Ready();

        lease.CloseSubmitGate();

        lease.SubmissionsReady.ShouldBeFalse();
    }

    [Test]
    public void LeaseLastsAPositiveTimeAndEpochStartsAtOne()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BotLease.Acquire(BotId.New(), First, Now, TimeSpan.Zero));
        Should.Throw<ArgumentOutOfRangeException>(() => BotLease.Restore(BotId.New(), First, epoch: 0, Now, submissionsReady: false));
    }

    private static BotLease Ready()
    {
        BotLease lease = BotLease.Acquire(BotId.New(), First, Now, Duration);
        lease.OpenSubmitGate(First, epoch: 1, Now);

        return lease;
    }
}