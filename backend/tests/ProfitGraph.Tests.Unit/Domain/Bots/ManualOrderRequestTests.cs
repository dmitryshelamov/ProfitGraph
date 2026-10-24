using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     Owner market orders (Profit Fox Designer ADR 0091).
/// </summary>
public sealed class ManualOrderRequestTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    [Test]
    public void NewRequestIsPending()
    {
        ManualOrderRequest request = Request();

        request.Status.ShouldBe(ManualOrderStatus.Pending);
        request.ResolvedAt.ShouldBeNull();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AmountMustBePositive(decimal amount)
    {
        Should.Throw<BotRuleException>(() => ManualOrderRequest.Create(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount, Now));
    }

    [Test]
    public void SubmittedRequestKeepsItsEngineOrder()
    {
        ManualOrderRequest request = Request();

        request.MarkSubmitted(engineOrderId: 12, Now);

        request.Status.ShouldBe(ManualOrderStatus.Submitted);
        request.EngineOrderId.ShouldBe(12);
        request.ResolvedAt.ShouldBe(Now);
    }

    [Test]
    public void RejectedRequestKeepsItsReason()
    {
        ManualOrderRequest request = Request();

        request.Reject(nameof(RejectionReason.InsufficientBase), Now);

        request.Status.ShouldBe(ManualOrderStatus.Rejected);
        request.RejectionReason.ShouldBe("InsufficientBase");
    }

    [Test]
    public void ResolvedRequestCannotResolveAgain()
    {
        ManualOrderRequest request = Request();
        request.MarkSubmitted(engineOrderId: 12, Now);

        Should.Throw<BotRuleException>(() => request.Reject("expired", Now)).Code.ShouldBe(BotRuleException.ManualOrderResolved);
        Should.Throw<BotRuleException>(() => request.MarkSubmitted(engineOrderId: 13, Now));
    }

    [Test]
    public void UntakenRequestExpiresAfterItsLifetime()
    {
        ManualOrderRequest request = Request();

        request.Expire(Now + ManualOrderRequest.Lifetime).ShouldBeFalse();
        request.Expire(Now + ManualOrderRequest.Lifetime + TimeSpan.FromTicks(1)).ShouldBeTrue();

        request.RejectionReason.ShouldBe("expired");
    }

    [Test]
    public void RestoreChecksStatusConsistency()
    {
        Should.Throw<ArgumentException>(() => ManualOrderRequest.Restore(
            ManualOrderRequestId.New(),
            BotId.New(),
            OrderSide.Sell,
            amount: 0.5m,
            ManualOrderStatus.Submitted,
            engineOrderId: null,
            rejectionReason: null,
            Now,
            Now));
    }

    private static ManualOrderRequest Request()
    {
        return ManualOrderRequest.Create(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, Now);
    }
}