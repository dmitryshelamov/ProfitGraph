using Grpc.Core;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Trading;

using Tinkoff.InvestApi.V1;

using OrderType = Tinkoff.InvestApi.V1.OrderType;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     T-Invest order execution (ADR 0023): every attempt is recorded before it may leave the process and is sent once in
///     whole lots under its deterministic request ID; an attempt that may have reached T-Invest is only looked up; broker
///     codes reject with their reason; fills are cumulative from the trade stages; reconciliation checks the account and
///     what else trades on the share. Values are invented; the API is the in-process mock.
/// </summary>
public sealed class TInvestOrderGatewayTests
{
    private const string AccountId = TInvestGatewayHarness.AccountId;
    private const string ShareUid = TInvestGatewayHarness.ShareUid;

    private MemoryTInvestOrderAttempts _attempts = null!;
    private BotId _bot;
    private TInvestOrderGateway _gateway = null!;
    private TInvestGatewayHarness _harness = null!;
    private MockTInvestOrders _orders = null!;

    [SetUp]
    public void CreateGateway()
    {
        _harness = new TInvestGatewayHarness();
        _bot = _harness.Bot;
        _orders = _harness.Orders;
        _attempts = _harness.Attempts;
        _gateway = _harness.Gateway;
    }

    [Test]
    public async Task ALimitOrderIsRecordedThenSentOnceAsADayOrderInLotsUnderItsRequestId()
    {
        var marked = 0;

        ExternalOrderUpdate? rejected = await _gateway.Submit(
            Submit(Limit(id: 1, OrderSide.Buy, quantity: 30m, price: 99.5m)),
            _ =>
            {
                marked++;
                _attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Planned);

                return Task.CompletedTask;
            },
            CancellationToken.None);

        rejected.ShouldBeNull();
        marked.ShouldBe(1);
        PostOrderRequest posted = _orders.Posted.ShouldHaveSingleItem();
        Guid requestId = TInvestOrderRequestIds.For(_bot, engineOrderId: 1, attempt: 1);
        posted.ShouldSatisfyAllConditions(
            () => posted.OrderId.ShouldBe(requestId.ToString("D")),
            () => posted.AccountId.ShouldBe(AccountId),
            () => posted.InstrumentId.ShouldBe(ShareUid),
            () => posted.Quantity.ShouldBe(3),
            () => posted.Price.ToDecimal().ShouldBe(99.5m),
            () => posted.Direction.ShouldBe(OrderDirection.Buy),
            () => posted.OrderType.ShouldBe(OrderType.Limit),
            () => posted.TimeInForce.ShouldBe(TimeInForceType.TimeInForceDay));
        TInvestOrderAttempt attempt = _attempts.All.ShouldHaveSingleItem();
        attempt.State.ShouldBe(TInvestOrderAttemptState.Active);
        attempt.ExchangeOrderId.ShouldBe(_orders.Order(posted.OrderId).OrderId);
        (await Next(count: 1)).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1)]);
    }

    [Test]
    public async Task AnAttemptWithoutAClearAnswerIsLookedUpByItsRequestId_AndNeverSentAgain()
    {
        _orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.Unavailable, "connection reset");
        _orders.PlaceBeforeFailing = true;

        await Should.ThrowAsync<TInvestOrderUnresolvedException>(() => _gateway.Submit(
            Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)),
            NoOp,
            CancellationToken.None));
        _attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Unknown);

        ExternalOrderUpdate? retried = await _gateway.Submit(
            Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m), mustLookUp: true),
            NoOp,
            CancellationToken.None);

        retried.ShouldBeNull();
        _orders.Posted.Count.ShouldBe(1);
        _attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Active);
        (await Next(count: 1)).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1)]);
    }

    [Test]
    public async Task AnAttemptTInvestDoesNotKnowStaysUnresolved_AndIsNotSentAgain()
    {
        _orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.DeadlineExceeded, "deadline");

        await Should.ThrowAsync<TInvestOrderUnresolvedException>(() => _gateway.Submit(
            Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)),
            NoOp,
            CancellationToken.None));
        await Should.ThrowAsync<TInvestOrderUnresolvedException>(() => _gateway.Submit(
            Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m), mustLookUp: true),
            NoOp,
            CancellationToken.None));

        _orders.Posted.Count.ShouldBe(1);
    }

    [Test]
    public async Task ABrokerCodeRejectsWithItsReason_AndTheRejectionIsKeptWithoutSendingAgain()
    {
        _orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30042");

        ExternalOrderUpdate? rejected = await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)), NoOp, CancellationToken.None);
        ExternalOrderUpdate? again = await _gateway.Submit(
            Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m), mustLookUp: true),
            NoOp,
            CancellationToken.None);

        ExternalOrderUpdate expected = ExternalOrderUpdate.Reject(orderId: 1, RejectionReason.RiskLimit, "T-Invest rejected the order (code 30042).");
        rejected.ShouldBe(expected);
        again.ShouldBe(expected);
        _orders.Posted.Count.ShouldBe(1);
        TInvestOrderAttempt attempt = _attempts.All.ShouldHaveSingleItem();
        attempt.ShouldSatisfyAllConditions(
            () => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
            () => attempt.BrokerCode.ShouldBe(30042),
            () => attempt.MayHaveReachedVenue.ShouldBeFalse());
    }

    [Test]
    public async Task AnUnknownBrokerCodeIsABrokerRejection_AndAStatusWithoutACodeIsUnknown()
    {
        _orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "39999");
        (await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Sell, quantity: 10m, price: 99m)), NoOp, CancellationToken.None))!.Reason.ShouldBe(
            RejectionReason.BrokerRejected);

        _orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "Недостаточно средств");
        await Should.ThrowAsync<TInvestOrderUnresolvedException>(() => _gateway.Submit(
            Submit(Limit(id: 2, OrderSide.Sell, quantity: 10m, price: 99m)),
            NoOp,
            CancellationToken.None));
        _attempts.All[1].State.ShouldBe(TInvestOrderAttemptState.Unknown);
    }

    [Test]
    public async Task AMarketBuyIsSizedInWholeLotsFromItsBudgetAtTheLastPricePlusSlippage()
    {
        _orders.LastPrice = 99m;

        await _gateway.Submit(Submit(MarketBuy(id: 1, budget: 10_100m)), NoOp, CancellationToken.None);

        PostOrderRequest posted = _orders.Posted.ShouldHaveSingleItem();
        posted.ShouldSatisfyAllConditions(
            () => posted.OrderType.ShouldBe(OrderType.Market),
            () => posted.Quantity.ShouldBe(10),
            () => posted.Price.ShouldBeNull());
    }

    [Test]
    public async Task AnOrderOfNoWholeLotOrOffTheTickIsRejectedBeforeAnythingIsRecorded()
    {
        (await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 15m, price: 99m)), NoOp, CancellationToken.None))!.Reason.ShouldBe(
            RejectionReason.InvalidAmount);
        (await _gateway.Submit(Submit(Limit(id: 2, OrderSide.Buy, quantity: 10m, price: 99.005m)), NoOp, CancellationToken.None))!.Reason.ShouldBe(
            RejectionReason.InvalidAmount);
        _orders.LastPrice = 200m;
        (await _gateway.Submit(Submit(MarketBuy(id: 3, budget: 1_000m)), NoOp, CancellationToken.None))!.Reason.ShouldBe(RejectionReason.InvalidAmount);

        _orders.Posted.ShouldBeEmpty();
        _attempts.All.ShouldBeEmpty();
    }

    [Test]
    public async Task FillsAreCumulativeFromTheTradeStages_AndAZeroCommissionIsPending()
    {
        (await Reconcile()).Degradation.ShouldBeNull();
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 30m, price: 100m)), NoOp, CancellationToken.None);
        string requestId = _orders.Posted.Single().OrderId;
        (await Next(count: 1)).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1)]);

        _orders.Fill(requestId, lots: 1, price: 99.5m, commission: 0m);
        IReadOnlyList<ExternalOrderUpdate> partial = await Until(update => update.Kind == ExternalOrderUpdateKind.PartialFill);
        _orders.Fill(requestId, lots: 2, price: 99.75m, commission: 2.99m);
        IReadOnlyList<ExternalOrderUpdate> final = await Until(update => update.Kind == ExternalOrderUpdateKind.FinalFill);

        partial[^1].ShouldBe(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 10m, grossQuote: 995m, fee: 0m, commissionPending: true, commissionPartitioned: true));
        final[^1].ShouldBe(ExternalOrderUpdate.Final(orderId: 1, grossBase: 30m, grossQuote: 2990m, fee: 2.99m, commissionPartitioned: true));
        _attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Final);
    }

    [Test]
    public async Task AnOrderTheServiceDoesNotKnowYetIsReadAgainBeforeItIsUnresolved()
    {
        (await Reconcile()).Degradation.ShouldBeNull();
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)), NoOp, CancellationToken.None);
        string requestId = _orders.Posted.Single().OrderId;
        (await Next(count: 1)).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1)]);
        _orders.StateNotFoundReads = 1;

        _orders.Fill(requestId, lots: 1, price: 99m, commission: 0.5m);

        (await Until(update => update.Kind == ExternalOrderUpdateKind.FinalFill))[^1]
            .ShouldBe(ExternalOrderUpdate.Final(orderId: 1, grossBase: 10m, grossQuote: 990m, fee: 0.5m, commissionPartitioned: true));
        _orders.StateNotFoundReads.ShouldBe(0);
    }

    [Test]
    public async Task ACancelGoesByRequestIdAndItsOutcomeIsReadBack_AndAnAttemptNeverSentCancelsLocally()
    {
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Sell, quantity: 10m, price: 101m)), NoOp, CancellationToken.None);
        await Next(count: 1);

        await _gateway.Cancel(_bot, _gateway.ClientOrderIdFor(_bot, engineOrderId: 1), CancellationToken.None);

        CancelOrderRequest cancel = _orders.Cancelled.ShouldHaveSingleItem();
        cancel.ShouldSatisfyAllConditions(
            () => cancel.OrderId.ShouldBe(TInvestOrderRequestIds.For(_bot, engineOrderId: 1, attempt: 1).ToString("D")),
            () => cancel.OrderIdType.ShouldBe(OrderIdType.Request));
        (await Next(count: 2)).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.ConfirmCancel(orderId: 1)]);

        await _gateway.Cancel(_bot, _gateway.ClientOrderIdFor(_bot, engineOrderId: 2), CancellationToken.None);
        (await Next(count: 1)).ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 2)]);
        _orders.Cancelled.Count.ShouldBe(1);
    }

    [Test]
    public async Task AFailedCancelOfAWorkingOrderIsUnresolved()
    {
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Sell, quantity: 10m, price: 101m)), NoOp, CancellationToken.None);
        _orders.NextCancelFailure = MockTInvestApi.Refusal(StatusCode.Unavailable, "connection reset");

        await Should.ThrowAsync<TInvestOrderUnresolvedException>(() => _gateway.Cancel(_bot, _gateway.ClientOrderIdFor(_bot, engineOrderId: 1), CancellationToken.None));
    }

    [Test]
    public async Task ReconciliationRefusesAnAccountWithoutFullAccess()
    {
        _orders.Access = AccessLevel.AccountAccessLevelReadOnly;

        (await Reconcile()).Degradation.ShouldBe(BotStatusReason.KeyPermissions);
    }

    [Test]
    public async Task ReconciliationReportsTheBotsOrders_AndRefusesAnotherOrderOnTheShare()
    {
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)), NoOp, CancellationToken.None);
        _orders.Fill(_orders.Posted.Single().OrderId, lots: 1, price: 99m, commission: 0.5m);

        BotReconciliationInternal mine = await Reconcile(
            new ReconcileOrderInternal(EngineOrderId: 1, _gateway.ClientOrderIdFor(_bot, engineOrderId: 1), MayHaveReachedVenue: true));

        mine.Degradation.ShouldBeNull();
        mine.Updates.ShouldBe(
            [ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.Final(orderId: 1, grossBase: 10m, grossQuote: 990m, fee: 0.5m, commissionPartitioned: true)]);
        mine.Found.ShouldHaveSingleItem().IsFinal.ShouldBeTrue();

        _orders.AddForeign(ShareUid);
        (await Reconcile()).Degradation.ShouldBe(BotStatusReason.ForeignOrder);
        (await Reconcile(allowForeignOrders: true)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task ReconciliationIsUnresolvedForAnAttemptTInvestDoesNotKnow()
    {
        _orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.DeadlineExceeded, "deadline");
        await Should.ThrowAsync<TInvestOrderUnresolvedException>(() => _gateway.Submit(
            Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)),
            NoOp,
            CancellationToken.None));

        BotReconciliationInternal reconciliation = await Reconcile(
            new ReconcileOrderInternal(EngineOrderId: 1, _gateway.ClientOrderIdFor(_bot, engineOrderId: 1), MayHaveReachedVenue: true));

        reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
    }

    [Test]
    public async Task AnAcceptedOrderTheExchangeRemovedIsCancelledOnlyWhenTheOwnerConfirmedItAndTInvestShowsNothing()
    {
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)), NoOp, CancellationToken.None);
        string requestId = _orders.Posted.Single().OrderId;
        _orders.Vanish(requestId);
        ClientOrderId client = _gateway.ClientOrderIdFor(_bot, engineOrderId: 1);

        (await Reconcile(new ReconcileOrderInternal(EngineOrderId: 1, client, MayHaveReachedVenue: true))).Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
        _attempts.All.Single().State.ShouldBe(TInvestOrderAttemptState.Active);

        BotReconciliationInternal confirmed =
            await Reconcile(new ReconcileOrderInternal(EngineOrderId: 1, client, MayHaveReachedVenue: true, OwnerConfirmedAbsent: true));

        confirmed.ShouldSatisfyAllConditions(
            () => confirmed.Degradation.ShouldBeNull(),
            () => confirmed.Updates.ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 1)]),
            () => confirmed.Found.ShouldHaveSingleItem().IsFinal.ShouldBeTrue(),
            () => _attempts.All.Single()
                .ShouldSatisfyAllConditions(
                    attempt => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
                    attempt => attempt.CancelledBy.ShouldBe(TInvestCancelReason.Owner),
                    attempt => attempt.ExecutedLots.ShouldBe(0)));
    }

    [Test]
    public async Task AnOrderTInvestStillKnowsByItsExchangeIdIsNotProvenRemoved()
    {
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)), NoOp, CancellationToken.None);
        _orders.Vanish(_orders.Posted.Single().OrderId, keptByExchangeId: true);

        BotReconciliationInternal reconciliation = await Reconcile(
            new ReconcileOrderInternal(EngineOrderId: 1, _gateway.ClientOrderIdFor(_bot, engineOrderId: 1), MayHaveReachedVenue: true, OwnerConfirmedAbsent: true));

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => reconciliation.Updates.ShouldBeEmpty(),
            () => _attempts.All.Single().State.ShouldBe(TInvestOrderAttemptState.Active));
    }

    [Test]
    public async Task AnEarlierAttemptThatExecutedLotsKeepsTheOrderUnresolvedEvenWhenTheOwnerConfirmedIt()
    {
        await _gateway.Submit(Submit(Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m)), NoOp, CancellationToken.None);
        TInvestOrderAttempt first = _attempts.All.Single();
        _orders.Vanish(first.RequestId.ToString("D"));
        await _attempts.Add(
            first with
            {
                Attempt = 2,
                RequestId = TInvestOrderRequestIds.For(_bot, engineOrderId: 1, attempt: 2),
                State = TInvestOrderAttemptState.Active,
                ExchangeOrderId = "7000099"
            },
            CancellationToken.None);
        await _attempts.Transition(first with { State = TInvestOrderAttemptState.Final, ExecutedLots = 3 }, TInvestOrderAttemptState.Active, CancellationToken.None);

        BotReconciliationInternal reconciliation = await Reconcile(
            new ReconcileOrderInternal(EngineOrderId: 1, _gateway.ClientOrderIdFor(_bot, engineOrderId: 1), MayHaveReachedVenue: true, OwnerConfirmedAbsent: true));

        reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
    }

    [Test]
    public void RequestIdsAreDeterministicUuidsPerAttempt()
    {
        Guid first = TInvestOrderRequestIds.For(_bot, engineOrderId: 7, attempt: 1);

        first.ShouldBe(TInvestOrderRequestIds.For(_bot, engineOrderId: 7, attempt: 1));
        first.ShouldNotBe(TInvestOrderRequestIds.For(_bot, engineOrderId: 7, attempt: 2));
        first.ShouldNotBe(TInvestOrderRequestIds.For(_bot, engineOrderId: 8, attempt: 1));
        first.ToString("D").Length.ShouldBe(36);
        first.Version.ShouldBe(8);
    }

    private Task<BotReconciliationInternal> Reconcile(params ReconcileOrderInternal[] orders)
    {
        return _gateway.Reconcile(new BotReconcileInternal(_bot, AllowForeignOrders: false, orders), CancellationToken.None);
    }

    private Task<BotReconciliationInternal> Reconcile(bool allowForeignOrders)
    {
        return _gateway.Reconcile(new BotReconcileInternal(_bot, allowForeignOrders, []), CancellationToken.None);
    }

    private OrderSubmitInternal Submit(OrderSnapshot order, bool mustLookUp = false)
    {
        return new OrderSubmitInternal(_bot, _gateway.ClientOrderIdFor(_bot, order.Id), order, mustLookUp);
    }

    private Task<IReadOnlyList<ExternalOrderUpdate>> Next(int count)
    {
        return _harness.Next(count);
    }

    private Task<IReadOnlyList<ExternalOrderUpdate>> Until(Func<ExternalOrderUpdate, bool> last)
    {
        return _harness.Until(last);
    }

    private static OrderSnapshot Limit(long id, OrderSide side, decimal quantity, decimal price)
    {
        return TInvestGatewayHarness.Limit(id, side, quantity, price);
    }

    private static OrderSnapshot MarketBuy(long id, decimal budget)
    {
        return TInvestGatewayHarness.MarketBuy(id, budget);
    }

    private static Task NoOp(CancellationToken cancellationToken)
    {
        return TInvestGatewayHarness.NoOp(cancellationToken);
    }
}