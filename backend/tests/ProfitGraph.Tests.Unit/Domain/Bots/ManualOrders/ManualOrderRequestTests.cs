using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.ManualOrders;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Domain.Bots.ManualOrders;

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
    public void MarketRequestHasNoPrice()
    {
        ManualOrderRequest request = Request();

        request.Type.ShouldBe(OrderType.Market);
        request.LimitPrice.ShouldBeNull();
    }

    [Test]
    public void LimitRequestKeepsItsPriceAsTyped()
    {
        ManualOrderRequest request = ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), BotId.New(), OrderSide.Sell, amount: 10m, limitPrice: 301.257m, Now);

        request.Type.ShouldBe(OrderType.Limit);
        request.LimitPrice.ShouldBe(301.257m);
        request.Status.ShouldBe(ManualOrderStatus.Pending);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void LimitPriceMustBePositive(decimal limitPrice)
    {
        Should.Throw<BotRuleException>(
            () => ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, limitPrice, Now));
    }

    [Test]
    public void LimitRequestResolvesLikeAMarketOne()
    {
        ManualOrderRequest submitted = ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, limitPrice: 4m, Now);
        ManualOrderRequest rejected = ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, limitPrice: 4m, Now);

        submitted.MarkSubmitted(engineOrderId: 7, Now);
        rejected.Reject(nameof(RejectionReason.TagActive), Now);

        submitted.Status.ShouldBe(ManualOrderStatus.Submitted);
        submitted.LimitPrice.ShouldBe(4m);
        rejected.Status.ShouldBe(ManualOrderStatus.Rejected);
        Should.Throw<BotRuleException>(() => submitted.Reject("expired", Now)).Code.ShouldBe(BotRuleException.ManualOrderResolved);
    }

    [Test]
    public void UntakenLimitRequestExpires()
    {
        ManualOrderRequest request = ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, limitPrice: 4m, Now);

        request.Expire(Now + ManualOrderRequest.Lifetime + TimeSpan.FromTicks(1)).ShouldBeTrue();

        request.RejectionReason.ShouldBe("expired");
    }

    [Test]
    public void RestoreChecksThePriceMatchesTheType()
    {
        Should.Throw<BotRuleException>(() => Restored(OrderType.Limit, limitPrice: null));
        Should.Throw<BotRuleException>(() => Restored(OrderType.Market, limitPrice: 4m));
        Restored(OrderType.Limit, limitPrice: 4m).LimitPrice.ShouldBe(4m);
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
            OrderType.Market,
            amount: 0.5m,
            limitPrice: null,
            ManualOrderStatus.Submitted,
            engineOrderId: null,
            rejectionReason: null,
            Now,
            Now));
    }

    [Test]
    public void RestoreChecksThatExactlyARejectionHasAReason()
    {
        Should.Throw<ArgumentException>(() => Resolved(ManualOrderStatus.Rejected, engineOrderId: null, rejectionReason: null));
        Should.Throw<ArgumentException>(() => Resolved(ManualOrderStatus.Submitted, engineOrderId: 7, "expired"));
        Resolved(ManualOrderStatus.Rejected, engineOrderId: null, "expired").RejectionReason.ShouldBe("expired");
        Resolved(ManualOrderStatus.Submitted, engineOrderId: 7, rejectionReason: null).EngineOrderId.ShouldBe(7);
    }

    private static ManualOrderRequest Resolved(ManualOrderStatus status, long? engineOrderId, string? rejectionReason)
    {
        return ManualOrderRequest.Restore(
            ManualOrderRequestId.New(),
            BotId.New(),
            OrderSide.Buy,
            OrderType.Market,
            amount: 25m,
            limitPrice: null,
            status,
            engineOrderId,
            rejectionReason,
            Now,
            Now);
    }

    private static ManualOrderRequest Restored(OrderType type, decimal? limitPrice)
    {
        return ManualOrderRequest.Restore(
            ManualOrderRequestId.New(),
            BotId.New(),
            OrderSide.Buy,
            type,
            amount: 25m,
            limitPrice,
            ManualOrderStatus.Pending,
            engineOrderId: null,
            rejectionReason: null,
            Now,
            resolvedAt: null);
    }

    private static ManualOrderRequest Request()
    {
        return ManualOrderRequest.Create(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, Now);
    }
}