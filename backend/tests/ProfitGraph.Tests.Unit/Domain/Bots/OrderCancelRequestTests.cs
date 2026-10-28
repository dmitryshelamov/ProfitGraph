using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     The owner's cancel of one of a bot's orders (ADR 0048).
/// </summary>
public sealed class OrderCancelRequestTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    [Test]
    public void NewRequestIsPending()
    {
        OrderCancelRequest request = Request();

        request.ShouldSatisfyAllConditions(
            () => request.Status.ShouldBe(OrderCancelStatus.Pending),
            () => request.EngineOrderId.ShouldBe(7),
            () => request.ResolvedAt.ShouldBeNull(),
            () => request.DomainEvents.ShouldBeEmpty());
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void EngineOrderMustBePositive(long engineOrderId)
    {
        Should.Throw<BotRuleException>(() => OrderCancelRequest.Create(OrderCancelRequestId.New(), BotId.New(), engineOrderId, Now));
    }

    [Test]
    public void RequestedCancelIsRecordedForTheFeed()
    {
        OrderCancelRequest request = Request();

        request.MarkRequested(Now);

        request.ShouldSatisfyAllConditions(
            () => request.Status.ShouldBe(OrderCancelStatus.Requested),
            () => request.ResolvedAt.ShouldBe(Now),
            () => request.RejectionReason.ShouldBeNull(),
            () => request.DomainEvents.ShouldBe([new OrderCancelRequested(request.BotId, request.Id, EngineOrderId: 7, Now)]));
    }

    [Test]
    public void RejectedRequestKeepsItsReasonAndRecordsNothing()
    {
        OrderCancelRequest request = Request();

        request.Reject("order_not_open", Now);

        request.ShouldSatisfyAllConditions(
            () => request.Status.ShouldBe(OrderCancelStatus.Rejected),
            () => request.RejectionReason.ShouldBe("order_not_open"),
            () => request.ResolvedAt.ShouldBe(Now),
            () => request.DomainEvents.ShouldBeEmpty());
    }

    [TestCase("")]
    [TestCase(" ")]
    public void RejectionNeedsAReason(string reason)
    {
        Should.Throw<ArgumentException>(() => Request().Reject(reason, Now));
    }

    [Test]
    public void ResolvedRequestCannotResolveAgain()
    {
        OrderCancelRequest requested = Request();
        OrderCancelRequest rejected = Request();
        requested.MarkRequested(Now);
        rejected.Reject("order_cancel_pending", Now);

        Should.Throw<BotRuleException>(() => requested.Reject("expired", Now)).Code.ShouldBe(BotRuleException.OrderCancelResolved);
        Should.Throw<BotRuleException>(() => requested.MarkRequested(Now)).Code.ShouldBe(BotRuleException.OrderCancelResolved);
        Should.Throw<BotRuleException>(() => rejected.MarkRequested(Now)).Code.ShouldBe(BotRuleException.OrderCancelResolved);
    }

    [Test]
    public void UntakenRequestExpiresAfterItsLifetime()
    {
        OrderCancelRequest request = Request();

        request.Expire(Now + OrderCancelRequest.Lifetime).ShouldBeFalse();
        request.Expire(Now + OrderCancelRequest.Lifetime + TimeSpan.FromTicks(1)).ShouldBeTrue();

        request.Status.ShouldBe(OrderCancelStatus.Rejected);
        request.RejectionReason.ShouldBe("expired");
    }

    [Test]
    public void ResolvedRequestDoesNotExpire()
    {
        OrderCancelRequest request = Request();
        request.MarkRequested(Now);

        request.IsExpired(Now + TimeSpan.FromHours(1)).ShouldBeFalse();
        request.Expire(Now + TimeSpan.FromHours(1)).ShouldBeFalse();
    }

    [Test]
    public void RestoreChecksStatusConsistency()
    {
        Should.Throw<ArgumentException>(() => Restored(OrderCancelStatus.Requested, rejectionReason: null, resolvedAt: null));
        Should.Throw<ArgumentException>(() => Restored(OrderCancelStatus.Rejected, rejectionReason: null, Now));
        Should.Throw<ArgumentException>(() => Restored(OrderCancelStatus.Pending, "expired", resolvedAt: null));
        Restored(OrderCancelStatus.Requested, rejectionReason: null, Now).Status.ShouldBe(OrderCancelStatus.Requested);
        Restored(OrderCancelStatus.Rejected, "expired", Now).RejectionReason.ShouldBe("expired");
    }

    private static OrderCancelRequest Restored(OrderCancelStatus status, string? rejectionReason, DateTimeOffset? resolvedAt)
    {
        return OrderCancelRequest.Restore(OrderCancelRequestId.New(), BotId.New(), engineOrderId: 7, status, rejectionReason, Now, resolvedAt);
    }

    private static OrderCancelRequest Request()
    {
        return OrderCancelRequest.Create(OrderCancelRequestId.New(), BotId.New(), engineOrderId: 7, Now);
    }
}