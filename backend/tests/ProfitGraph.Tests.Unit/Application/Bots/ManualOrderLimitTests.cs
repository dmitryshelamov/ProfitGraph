using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Bots.SubmitManualOrder;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.ManualOrders;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     The limit variant of the owner's manual order (ADR 0046): its command is checked and its request reads back with the price.
/// </summary>
public sealed class ManualOrderLimitTests
{
    private static readonly DateTimeOffset Now = BotFixture.Created;

    [Test]
    public void LimitCommandWithAPositivePriceIsValid()
    {
        new SubmitManualOrderCommandValidator().Validate(Command(limitPrice: 301.25m)).IsValid.ShouldBeTrue();
        new SubmitManualOrderCommandValidator().Validate(Command(limitPrice: null)).IsValid.ShouldBeTrue();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NonPositivePriceIsInvalid(decimal limitPrice)
    {
        new SubmitManualOrderCommandValidator().Validate(Command(limitPrice)).IsValid.ShouldBeFalse();
    }

    [Test]
    public void LimitRequestReadsBackWithTypeAndPrice()
    {
        var request = ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), BotId.New(), OrderSide.Sell, amount: 10m, limitPrice: 301.25m, Now);

        ManualOrderInternal order = request.ToInternal(Now);

        order.Type.ShouldBe(OrderType.Limit);
        order.LimitPrice.ShouldBe(301.25m);
        order.Amount.ShouldBe(10m);
        order.Status.ShouldBe(ManualOrderStatus.Pending);
    }

    [Test]
    public void MarketRequestReadsBackWithoutPrice()
    {
        var request = ManualOrderRequest.Create(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, Now);

        ManualOrderInternal order = request.ToInternal(Now);

        order.Type.ShouldBe(OrderType.Market);
        order.LimitPrice.ShouldBeNull();
    }

    [Test]
    public void UntakenLimitRequestReadsAsExpiredEvenBeforeItIsStoredSo()
    {
        var request = ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), BotId.New(), OrderSide.Buy, amount: 25m, limitPrice: 4m, Now);

        ManualOrderInternal order = request.ToInternal(Now + ManualOrderRequest.Lifetime + TimeSpan.FromSeconds(1));

        order.Status.ShouldBe(ManualOrderStatus.Rejected);
        order.RejectionReason.ShouldBe("expired");
        order.LimitPrice.ShouldBe(4m);
    }

    private static SubmitManualOrderCommand Command(decimal? limitPrice)
    {
        return new SubmitManualOrderCommand(Guid.NewGuid(), Guid.NewGuid(), OrderSide.Buy, Amount: 10m, limitPrice);
    }
}