using Grpc.Core;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Trading;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     Orders T-Invest cannot take now wait locally (ADR 0023, T12; Profit Fox Designer ADR 0072): a closed trading status
///     or a limit price outside the band keeps the engine order working without a request; the worker sends it once the
///     status and the band allow and the core lets the bot submit; a favourable bound is used for an aggressive price; a
///     definitive <c>30099</c> returns the order to waiting as the next attempt; a waiting order cancels locally.
/// </summary>
public sealed class TInvestWaitingOrderTests
{
    private TInvestGatewayHarness _harness = null!;

    [SetUp]
    public void CreateGateway()
    {
        _harness = new TInvestGatewayHarness();
    }

    [Test]
    public async Task AClosedSessionKeepsTheOrderWaitingLocally_AndNothingIsSent()
    {
        _harness.Orders.TradingOpen = false;

        ExternalOrderUpdate? rejected = await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m));

        rejected.ShouldBeNull();
        _harness.Orders.Posted.ShouldBeEmpty();
        (await _harness.Next(count: 1)).ShouldBe([ExternalOrderUpdate.AcceptLocally(orderId: 1)]);
        _harness.Waits.Reports.ShouldBe([(1L, OrderWaitingCode.SessionClosed, null, null)]);
        TInvestOrderAttempt attempt = _harness.Attempts.All.ShouldHaveSingleItem();
        attempt.ShouldSatisfyAllConditions(
            () => attempt.State.ShouldBe(TInvestOrderAttemptState.Planned),
            () => attempt.WaitingCode.ShouldBe(OrderWaitingCode.SessionClosed),
            () => attempt.MayHaveReachedVenue.ShouldBeFalse());
    }

    [Test]
    public async Task TheWorkerSendsAWaitingOrderOnceTheSessionOpens_AndEndsItsWait()
    {
        _harness.Orders.TradingOpen = false;
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m));
        await _harness.Next(count: 1);
        BotReconciliationInternal reconciled = await Reconcile(engineOrderId: 1);
        reconciled.Updates.ShouldBe([ExternalOrderUpdate.AcceptLocally(orderId: 1)]);
        reconciled.Found.ShouldHaveSingleItem().IsFinal.ShouldBeFalse();

        await _harness.Worker.Round(CancellationToken.None);
        _harness.Orders.Posted.ShouldBeEmpty();

        _harness.Orders.TradingOpen = true;
        await _harness.Worker.Round(CancellationToken.None);
        await _harness.Worker.Round(CancellationToken.None);

        PostOrderRequest posted = _harness.Orders.Posted.ShouldHaveSingleItem();
        posted.OrderId.ShouldBe(TInvestOrderRequestIds.For(_harness.Bot, engineOrderId: 1, attempt: 1).ToString("D"));
        _harness.Waits.Reports[^1].ShouldBe((1L, (OrderWaitingCode?)null, (decimal?)null, (decimal?)null));
        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.Accepted))[^1].ShouldBe(ExternalOrderUpdate.Accept(orderId: 1));
        _harness.Attempts.All.ShouldHaveSingleItem().WaitingCode.ShouldBeNull();
    }

    [Test]
    public async Task AWaitingOrderIsNotSentWhileTheCoreRefusesTheBot_OrItsRunnerIsNotReading()
    {
        _harness.Orders.TradingOpen = false;
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m));
        _harness.Orders.TradingOpen = true;

        await _harness.Worker.Round(CancellationToken.None);
        _harness.Orders.Posted.ShouldBeEmpty();

        await Reconcile(engineOrderId: 1);
        _harness.Waits.MaySend = false;
        await _harness.Worker.Round(CancellationToken.None);

        _harness.Orders.Posted.ShouldBeEmpty();
        _harness.Attempts.All.ShouldHaveSingleItem().WaitingCode.ShouldBe(OrderWaitingCode.SessionClosed);
    }

    [Test]
    public async Task ThePriceBandHoldsPassivePrices_AndSendsAggressiveOnesAtTheFavourableBound()
    {
        _harness.Orders.Band = (90m, 110m);

        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 80m));
        await Submit(TInvestGatewayHarness.Limit(id: 2, OrderSide.Sell, quantity: 10m, price: 120m));
        await Submit(TInvestGatewayHarness.Limit(id: 3, OrderSide.Buy, quantity: 10m, price: 120m));
        await Submit(TInvestGatewayHarness.Limit(id: 4, OrderSide.Sell, quantity: 10m, price: 80m));
        await Submit(TInvestGatewayHarness.Limit(id: 5, OrderSide.Buy, quantity: 10m, price: 110m));

        _harness.Orders.Posted.Select(posted => posted.Price.ToDecimal()).ShouldBe([110m, 90m, 110m]);
        _harness.Waits.Reports.ShouldBe(
        [
            (1L, OrderWaitingCode.PriceBand, 90m, 110m),
            (2L, OrderWaitingCode.PriceBand, 90m, 110m)
        ]);
        _harness.Attempts.All.Where(attempt => attempt.State == TInvestOrderAttemptState.Planned).Select(attempt => attempt.EngineOrderId).ShouldBe([1L, 2L]);
        _harness.Orders.BookReads.ShouldBe(1);
    }

    [Test]
    public async Task BandMovesThatDoNotReachThePriceSendNothing_AndOneThatDoesSendsOnce()
    {
        _harness.Orders.Band = (90m, 110m);
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 80m));
        await Reconcile(engineOrderId: 1);

        foreach ((decimal low, decimal high) in new[] { (91m, 111m), (92m, 112m) })
        {
            _harness.Orders.Band = (low, high);
            _harness.Clock.Advance(TimeSpan.FromSeconds(6));
            await _harness.Worker.Round(CancellationToken.None);
        }

        _harness.Orders.Posted.ShouldBeEmpty();
        _harness.Waits.Reports[^1].ShouldBe((1L, (OrderWaitingCode?)OrderWaitingCode.PriceBand, (decimal?)92m, (decimal?)112m));

        _harness.Orders.Band = (80m, 100m);
        _harness.Clock.Advance(TimeSpan.FromSeconds(6));
        await _harness.Worker.Round(CancellationToken.None);
        _harness.Clock.Advance(TimeSpan.FromSeconds(6));
        await _harness.Worker.Round(CancellationToken.None);

        _harness.Orders.Posted.ShouldHaveSingleItem().Price.ToDecimal().ShouldBe(80m);
    }

    [Test]
    public async Task AMissingBandWaits()
    {
        _harness.Orders.Band = null;

        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m));

        _harness.Orders.Posted.ShouldBeEmpty();
        _harness.Waits.Reports.ShouldBe([(1L, OrderWaitingCode.PriceBand, null, null)]);
    }

    [Test]
    public async Task ADefinitiveBandRefusalReturnsTheOrderToWaitingAsTheNextAttempt_WithoutARejection()
    {
        _harness.Orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30099");

        ExternalOrderUpdate? rejected = await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m));

        rejected.ShouldBeNull();
        (await _harness.Next(count: 1)).ShouldBe([ExternalOrderUpdate.AcceptLocally(orderId: 1)]);
        _harness.Attempts.All.Select(attempt => (attempt.Attempt, attempt.State, attempt.BrokerCode, attempt.WaitingCode))
            .ShouldBe(
            [
                (1, TInvestOrderAttemptState.Final, 30099, null),
                (2, TInvestOrderAttemptState.Planned, (int?)null, (OrderWaitingCode?)OrderWaitingCode.PriceBand)
            ]);

        await Reconcile(engineOrderId: 1);
        await _harness.Worker.Round(CancellationToken.None);

        _harness.Orders.Posted.Select(posted => posted.OrderId)
            .ShouldBe(
            [
                TInvestOrderRequestIds.For(_harness.Bot, engineOrderId: 1, attempt: 1).ToString("D"),
                TInvestOrderRequestIds.For(_harness.Bot, engineOrderId: 1, attempt: 2).ToString("D")
            ]);
    }

    [Test]
    public async Task AWaitingOrderCancelsLocally()
    {
        _harness.Orders.TradingOpen = false;
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m));
        await _harness.Next(count: 1);

        await _harness.Gateway.Cancel(_harness.Bot, _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId: 1), CancellationToken.None);

        (await _harness.Next(count: 1)).ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 1)]);
        _harness.Orders.Cancelled.ShouldBeEmpty();
        _harness.Attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Final);
    }

    private Task<ExternalOrderUpdate?> Submit(OrderSnapshot order)
    {
        return _harness.Gateway.Submit(
            new OrderSubmitInternal(_harness.Bot, _harness.Gateway.ClientOrderIdFor(_harness.Bot, order.Id), order, MustLookUpBeforeSubmit: false),
            TInvestGatewayHarness.NoOp,
            CancellationToken.None);
    }

    private Task<BotReconciliationInternal> Reconcile(long engineOrderId)
    {
        return _harness.Gateway.Reconcile(
            new BotReconcileInternal(
                _harness.Bot,
                AllowForeignOrders: false,
                [new ReconcileOrderInternal(engineOrderId, _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId), MayHaveReachedVenue: true)]),
            CancellationToken.None);
    }
}