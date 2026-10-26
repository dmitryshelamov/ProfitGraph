using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     The order outbox (Profit Fox Designer ADRs 0054–0056): claims, delivery outcomes, retries and kills.
/// </summary>
public sealed class OrderIntentTransitionsTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;
    private static readonly BotId Bot = BotId.New();

    [Test]
    public void NewIntentIsPending()
    {
        OrderIntent intent = Submit();

        intent.ShouldSatisfyAllConditions(
            () => intent.Status.ShouldBe(OrderIntentStatus.Pending),
            () => intent.Kind.ShouldBe(OrderIntentKind.Submit),
            () => intent.EngineOrderId.ShouldBe(7),
            () => intent.MustLookUpBeforeSubmit.ShouldBeFalse(),
            () => intent.CanClaim(Now).ShouldBeTrue());
    }

    [Test]
    public void SubmitNeedsItsOrderAndCancelNone()
    {
        Should.Throw<ArgumentNullException>(() => OrderIntent.Submit(OrderIntentId.New(), Bot, null!, new ClientOrderId("c-1"), Now));
        Should.Throw<BotRuleException>(() => OrderIntent.Cancel(OrderIntentId.New(), Bot, engineOrderId: 0, new ClientOrderId("c-1"), fromKill: false, Now));
        OrderIntent.Cancel(OrderIntentId.New(), Bot, engineOrderId: 7, new ClientOrderId("c-1"), fromKill: true, Now).Order.ShouldBeNull();
    }

    [TestCase("")]
    [TestCase("with space")]
    [TestCase("тест")]
    public void ClientOrderIdIsVisibleAscii(string value)
    {
        Should.Throw<BotRuleException>(() => new ClientOrderId(value));
    }

    [Test]
    public void ClaimMovesToSending()
    {
        OrderIntent intent = Submit();

        intent.Claim(Now);

        intent.Status.ShouldBe(OrderIntentStatus.Sending);
        intent.ClaimedAt.ShouldBe(Now);
        intent.CanClaim(Now).ShouldBeFalse();
    }

    [Test]
    public void StaleClaimCanBeTakenAgain()
    {
        OrderIntent intent = Claimed();

        intent.CanClaim(Now + OrderIntentRetrySchedule.StaleClaimAfter - TimeSpan.FromTicks(1)).ShouldBeFalse();
        intent.CanClaim(Now + OrderIntentRetrySchedule.StaleClaimAfter).ShouldBeTrue();
    }

    [Test]
    public void FinishedIntentCannotBeClaimed()
    {
        OrderIntent intent = Claimed();
        intent.MarkSent(Now);

        Should.Throw<BotRuleException>(() => intent.Claim(Now.AddHours(1))).Code.ShouldBe(BotRuleException.InvalidIntentTransition);
    }

    [Test]
    public void PlaceStartIsRecordedOnceAndForcesALookup()
    {
        OrderIntent intent = Claimed();

        intent.MarkPlaceStarted(Now);
        intent.MarkPlaceStarted(Now.AddSeconds(1));

        intent.PlaceStartedAt.ShouldBe(Now);
        intent.MustLookUpBeforeSubmit.ShouldBeTrue();
        intent.DomainEvents.ShouldBe([new OrderIntentPlaceStarted(Bot, intent.Id, EngineOrderId: 7, Now)]);
    }

    [Test]
    public void OnlyAClaimedSubmitStartsAPlace()
    {
        Should.Throw<BotRuleException>(() => Submit().MarkPlaceStarted(Now));
        OrderIntent cancel = Cancel();
        cancel.Claim(Now);
        Should.Throw<BotRuleException>(() => cancel.MarkPlaceStarted(Now));
    }

    [Test]
    public void SentIntentIsFinished()
    {
        OrderIntent intent = Claimed();

        intent.MarkSent(Now);

        intent.Status.ShouldBe(OrderIntentStatus.Sent);
        intent.SentAt.ShouldBe(Now);
        intent.ClaimedAt.ShouldBeNull();
    }

    [Test]
    public void OnlyAClaimedIntentIsSent()
    {
        Should.Throw<BotRuleException>(() => Submit().MarkSent(Now));
    }

    [Test]
    public void RejectionIsDefinitive()
    {
        OrderIntent intent = Claimed();

        intent.Reject(new OrderIntentRejection(RejectionReason.RiskLimit, "Order exceeds the limit."), Now);

        intent.ShouldSatisfyAllConditions(
            () => intent.Status.ShouldBe(OrderIntentStatus.Rejected),
            () => intent.Rejection!.Reason.ShouldBe(RejectionReason.RiskLimit),
            () => intent.DomainEvents.ShouldBe([new OrderIntentRejected(Bot, intent.Id, EngineOrderId: 7, RejectionReason.RiskLimit, Now)]));
    }

    [Test]
    public void RejectionMessageIsTrimmedAndBounded()
    {
        new OrderIntentRejection(RejectionReason.BrokerRejected, new string(c: 'x', count: 600)).Message.Length.ShouldBe(OrderIntentRejection.MaxMessageLength);
        new OrderIntentRejection(RejectionReason.BrokerRejected, message: null).Message.ShouldBe(string.Empty);
    }

    [Test]
    public void CancelIsNeverRejected()
    {
        OrderIntent cancel = Cancel();
        cancel.Claim(Now);

        Should.Throw<BotRuleException>(() => cancel.Reject(new OrderIntentRejection(RejectionReason.RiskLimit, "no"), Now));
    }

    [Test]
    public void FailuresBackOffThenFailTheIntent()
    {
        OrderIntent intent = Submit();
        DateTimeOffset now = Now;
        var delays = new List<TimeSpan>();

        for (var attempt = 1; attempt < OrderIntentRetrySchedule.MaxAttempts; attempt++)
        {
            intent.Claim(now);
            intent.RecordFailure(now);
            delays.Add(intent.NextAttemptAt!.Value - now);
            now = intent.NextAttemptAt.Value;
        }

        intent.Claim(now);
        intent.RecordFailure(now);

        delays.ShouldBe(
        [
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)
        ]);
        intent.ShouldSatisfyAllConditions(
            () => intent.Status.ShouldBe(OrderIntentStatus.Failed),
            () => intent.Attempts.ShouldBe(OrderIntentRetrySchedule.MaxAttempts),
            () => intent.NextAttemptAt.ShouldBeNull(),
            () => intent.DomainEvents.ShouldBe([new OrderIntentFailed(Bot, intent.Id, EngineOrderId: 7, now)]));
    }

    [Test]
    public void FailedAttemptWaitsForItsTime()
    {
        OrderIntent intent = Claimed();

        intent.RecordFailure(Now);

        intent.CanClaim(Now).ShouldBeFalse();
        intent.CanClaim(Now.AddSeconds(1)).ShouldBeTrue();
    }

    [Test]
    public void TransientRetryCountsNoFailure()
    {
        OrderIntent intent = Claimed();

        intent.RetryLater(Now + OrderIntentRetrySchedule.TransientRetryDelay);

        intent.Status.ShouldBe(OrderIntentStatus.Pending);
        intent.Attempts.ShouldBe(0);
        intent.NextAttemptAt.ShouldBe(Now.AddSeconds(5));
    }

    [Test]
    public void ReleasedIntentWaitsAgain()
    {
        OrderIntent intent = Claimed();

        intent.Release(submitsKilled: false, Now);

        intent.Status.ShouldBe(OrderIntentStatus.Pending);
        intent.CanClaim(Now).ShouldBeTrue();
    }

    [Test]
    public void KilledReleaseRejectsAnUnplacedSubmit()
    {
        OrderIntent intent = Claimed();

        intent.Release(submitsKilled: true, Now);

        intent.Status.ShouldBe(OrderIntentStatus.Rejected);
        intent.Rejection!.Reason.ShouldBe(RejectionReason.KillSwitch);
    }

    [Test]
    public void KilledReleaseKeepsAPossiblyPlacedSubmit()
    {
        OrderIntent intent = Claimed();
        intent.MarkPlaceStarted(Now);

        intent.Release(submitsKilled: true, Now);

        intent.Status.ShouldBe(OrderIntentStatus.Pending);
        intent.MustLookUpBeforeSubmit.ShouldBeTrue();
    }

    [Test]
    public void KilledReleaseKeepsACancel()
    {
        OrderIntent cancel = Cancel();
        cancel.Claim(Now);

        cancel.Release(submitsKilled: true, Now);

        cancel.Status.ShouldBe(OrderIntentStatus.Pending);
    }

    [Test]
    public void KillRejectsWaitingAndFailedUnplacedSubmits()
    {
        OrderIntent waiting = Submit();
        OrderIntent failed = FailedUnplaced();

        waiting.RejectForKill(Now).ShouldBeTrue();
        failed.RejectForKill(Now).ShouldBeTrue();

        waiting.Rejection!.Reason.ShouldBe(RejectionReason.KillSwitch);
        failed.Status.ShouldBe(OrderIntentStatus.Rejected);
    }

    [Test]
    public void KillLeavesPlacedSubmitsCancelsAndClaimsAlone()
    {
        OrderIntent placed = Claimed();
        placed.MarkPlaceStarted(Now);
        placed.RecordFailure(Now);
        OrderIntent claimed = Claimed();

        placed.RejectForKill(Now).ShouldBeFalse();
        Cancel().RejectForKill(Now).ShouldBeFalse();
        claimed.RejectForKill(Now).ShouldBeFalse();
    }

    [Test]
    public void ReconciliationResolvesAnUndeliveredIntent()
    {
        OrderIntent failed = FailedUnplaced();

        failed.Resolve(Now);

        failed.Status.ShouldBe(OrderIntentStatus.Sent);
    }

    [Test]
    public void FinishedIntentIsNotResolved()
    {
        OrderIntent intent = Claimed();
        intent.MarkSent(Now);

        Should.Throw<BotRuleException>(() => intent.Resolve(Now));
    }

    [Test]
    public void UnsentFailureCanStartOver()
    {
        OrderIntent failed = FailedUnplaced();

        failed.ResetUnsentFailure().ShouldBeTrue();

        failed.Status.ShouldBe(OrderIntentStatus.Pending);
        failed.Attempts.ShouldBe(0);
    }

    [Test]
    public void PossiblyPlacedFailureIsNotReset()
    {
        OrderIntent intent = Submit();

        for (var attempt = 0; attempt < OrderIntentRetrySchedule.MaxAttempts; attempt++)
        {
            intent.Claim(Now.AddHours(attempt));
            intent.MarkPlaceStarted(Now);
            intent.RecordFailure(Now.AddHours(attempt));
        }

        intent.ResetUnsentFailure().ShouldBeFalse();
        intent.Status.ShouldBe(OrderIntentStatus.Failed);
    }

    [Test]
    public void SnapshotRestoresTheSameIntent()
    {
        OrderIntent intent = Claimed();
        intent.MarkPlaceStarted(Now);
        intent.RecordFailure(Now);

        OrderIntent restored = OrderIntent.Restore(intent.ToSnapshot());

        restored.ToSnapshot().ShouldBe(intent.ToSnapshot());
    }

    [Test]
    public void AnOwnerConfirmsAPlacedSubmitAbsentOnce()
    {
        OrderIntent intent = Placed();
        intent.RecordFailure(Now);
        intent.ClearDomainEvents();
        var owner = UserId.New();

        intent.ConfirmAbsent(owner, Now);

        intent.ShouldSatisfyAllConditions(
            () => intent.Status.ShouldBe(OrderIntentStatus.Failed),
            () => intent.ConfirmedAbsentAt.ShouldBe(Now),
            () => intent.ConfirmedAbsentBy.ShouldBe(owner),
            () => intent.NextAttemptAt.ShouldBeNull(),
            () => intent.CanClaim(Now + TimeSpan.FromDays(1)).ShouldBeFalse(),
            () => intent.DomainEvents.ShouldBe([new OrderIntentAbsenceConfirmed(Bot, intent.Id, EngineOrderId: 7, owner, Now)]));
        Should.Throw<BotRuleException>(() => intent.ConfirmAbsent(owner, Now));
    }

    [Test]
    public void AConfirmedSubmitIsResolvedByReconciliation()
    {
        OrderIntent intent = Placed();
        intent.RecordFailure(Now);
        intent.ConfirmAbsent(UserId.New(), Now);

        intent.Resolve(Now);

        intent.Status.ShouldBe(OrderIntentStatus.Sent);
    }

    [Test]
    public void OnlyAnUndeliveredSubmitThatMayHaveLeftIsConfirmedAbsent()
    {
        var owner = UserId.New();
        OrderIntent sent = Claimed();
        sent.MarkPlaceStarted(Now);
        sent.MarkSent(Now);
        OrderIntent sending = Placed();

        Should.Throw<BotRuleException>(() => Submit().ConfirmAbsent(owner, Now));
        Should.Throw<BotRuleException>(() => Cancel().ConfirmAbsent(owner, Now));
        Should.Throw<BotRuleException>(() => sent.ConfirmAbsent(owner, Now));
        Should.Throw<BotRuleException>(() => sending.ConfirmAbsent(owner, Now));
    }

    [Test]
    public void DeferHoldsAClaimedSubmitUntilItsNextCheck()
    {
        OrderIntent intent = Claimed();
        DateTimeOffset next = Now.AddMinutes(10);

        bool started = intent.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now, next), next, Now);

        intent.ShouldSatisfyAllConditions(
            () => started.ShouldBeTrue(),
            () => intent.Status.ShouldBe(OrderIntentStatus.Deferred),
            () => intent.ClaimedAt.ShouldBeNull(),
            () => intent.NextAttemptAt.ShouldBe(next),
            () => intent.CanClaim(Now).ShouldBeFalse(),
            () => intent.CanClaim(next).ShouldBeTrue(),
            () => intent.DomainEvents.OfType<OrderIntentDeferred>().ShouldHaveSingleItem().Code.ShouldBe(OrderWaitingCode.SettlementHold));
    }

    [Test]
    public void ADeferredSubmitIsClaimedAgainAndKeepsTheStartOfItsSameWait()
    {
        OrderIntent intent = Claimed();
        intent.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now);
        intent.Claim(Now.AddMinutes(1));
        intent.ClearDomainEvents();

        bool started = intent.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now.AddMinutes(1), Now.AddHours(1)), Now.AddMinutes(2), Now.AddMinutes(1));

        intent.ShouldSatisfyAllConditions(
            () => started.ShouldBeFalse(),
            () => intent.Deferral!.Since.ShouldBe(Now),
            () => intent.Deferral!.Until.ShouldBe(Now.AddHours(1)),
            () => intent.DomainEvents.ShouldBeEmpty());
    }

    [Test]
    public void ANewReasonStartsANewWait()
    {
        OrderIntent intent = Claimed();
        intent.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now);
        intent.Claim(Now.AddMinutes(1));

        intent.Defer(new OrderDeferral(OrderWaitingCode.RateLimited, Now.AddMinutes(1)), Now.AddMinutes(2), Now.AddMinutes(1)).ShouldBeTrue();

        intent.Deferral!.Since.ShouldBe(Now.AddMinutes(1));
    }

    [Test]
    public void DeferIsRefusedAfterAPlaceStartAndForAnInvalidWait()
    {
        Should.Throw<BotRuleException>(() => Placed().Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now));
        Should.Throw<BotRuleException>(() => Claimed().Defer(new OrderDeferral((OrderWaitingCode)99, Now), Now.AddMinutes(1), Now));
        Should.Throw<BotRuleException>(() => Submit().Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now));
    }

    [Test]
    public void KillRejectsADeferredSubmit()
    {
        OrderIntent intent = Claimed();
        intent.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now);

        intent.RejectForKill(Now).ShouldBeTrue();

        intent.ShouldSatisfyAllConditions(
            () => intent.Status.ShouldBe(OrderIntentStatus.Rejected),
            () => intent.Rejection!.Reason.ShouldBe(RejectionReason.KillSwitch),
            () => intent.Deferral.ShouldBeNull());
    }

    [Test]
    public void ACancelOfADeferredSubmitRejectsItLocally()
    {
        OrderIntent intent = Claimed();
        intent.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now);

        intent.CancelDeferred(Now);

        intent.Rejection!.Reason.ShouldBe(RejectionReason.CancelledBeforeSend);
        Should.Throw<BotRuleException>(() => Submit().CancelDeferred(Now));
    }

    [Test]
    public void ADeferredSubmitIsNotRestoredWithoutItsReason()
    {
        OrderIntent intent = Claimed();
        intent.Defer(new OrderDeferral(OrderWaitingCode.SettlementHold, Now), Now.AddMinutes(1), Now);
        OrderIntentSnapshot snapshot = intent.ToSnapshot();

        OrderIntent.Restore(snapshot).Deferral.ShouldBe(snapshot.Deferral);
        Should.Throw<ArgumentException>(() => OrderIntent.Restore(snapshot with { Deferral = null }));
    }

    private static OrderIntent Placed()
    {
        OrderIntent intent = Claimed();
        intent.MarkPlaceStarted(Now);

        return intent;
    }

    private static OrderIntent Submit()
    {
        var order = new OrderSnapshot(
            Id: 7,
            "entry",
            OrderSide.Buy,
            OrderType.Market,
            Quantity: null,
            LimitPrice: null,
            QuoteBudget: 10m,
            Reservation: 10m,
            "entry",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);

        return OrderIntent.Submit(OrderIntentId.New(), Bot, order, new ClientOrderId("pg-test-7"), Now);
    }

    private static OrderIntent Cancel()
    {
        return OrderIntent.Cancel(OrderIntentId.New(), Bot, engineOrderId: 7, new ClientOrderId("pg-test-7"), fromKill: false, Now);
    }

    private static OrderIntent Claimed()
    {
        OrderIntent intent = Submit();
        intent.Claim(Now);
        intent.ClearDomainEvents();

        return intent;
    }

    private static OrderIntent FailedUnplaced()
    {
        OrderIntent intent = Submit();

        for (var attempt = 0; attempt < OrderIntentRetrySchedule.MaxAttempts; attempt++)
        {
            intent.Claim(Now.AddHours(attempt));
            intent.RecordFailure(Now.AddHours(attempt));
        }

        intent.Status.ShouldBe(OrderIntentStatus.Failed);

        return intent;
    }
}