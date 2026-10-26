using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

public sealed class ExternallyExecutedAccountTests
{
    private static readonly ExecutionSettings Settings = new ExecutionSettings(startingCapital: 100m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.01m,
        priceTick: 0.01m,
        minQuantity: 0.01m,
        minNotional: 1m,
        basePrecision: 2,
        quotePrecision: 2);

    [Test]
    public void PendingSubmitReservesFundsOccupiesTagAndCanAwaitCancellation()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var request = new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 25m, LimitPrice: 5m, "entry", "buy", EligibleFromCandleIndex: 1);
        Order order = account.Submit(request).Order.ShouldNotBeNull();
        order.Status.ShouldBe(OrderStatus.PendingSubmit);
        account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingSubmit);
        account.Capture().QuoteReserved.ShouldBe(25m);
        account.Capture().QuoteAvailable.ShouldBe(75m);
        account.Submit(request).Rejection.ShouldNotBeNull().Reason.ShouldBe(RejectionReason.TagActive);

        account.RequestCancelByTag("entry").ShouldBe(new ExternalCancelRequest(ExternalCancelDisposition.Pending, order.Id));
        order.Status.ShouldBe(OrderStatus.PendingCancel);
        account.Capture().Orders.Single().Status.ShouldBe(OrderStatus.PendingCancel);
        account.RequestCancelByTag("entry").ShouldBe(new ExternalCancelRequest(ExternalCancelDisposition.AlreadyPending, order.Id));
        account.Capture().QuoteReserved.ShouldBe(25m);
    }

    [Test]
    public void ARunThatStartsOverContinuesTheOrderIdentifiersBeforeItsFirstOrder()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var request = new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 25m, LimitPrice: 5m, "entry", "buy", EligibleFromCandleIndex: 1);

        account.ContinueOrderIdsAfter(lastIssuedId: 7);

        account.Submit(request).Order.ShouldNotBeNull().Id.ShouldBe(8);
        Should.Throw<ArgumentException>(() => account.ContinueOrderIdsAfter(lastIssuedId: 20));
    }

    [Test]
    public void ReplayAccountStillSubmitsAndCancelsSynchronously()
    {
        var account = new TradingAccount(Settings, Rules);
        var request = new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 25m, LimitPrice: 5m, "entry", "buy", EligibleFromCandleIndex: 1);
        Order order = account.Submit(request).Order.ShouldNotBeNull();
        order.Status.ShouldBe(OrderStatus.Active);
        account.Cancel(order.Id).ShouldBeSameAs(order);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        account.Capture().QuoteReserved.ShouldBe(0m);
    }
}