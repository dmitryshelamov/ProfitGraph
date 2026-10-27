using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Trading;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     An accepted DAY order T-Invest no longer finds is closed without the owner only when the broker's own records prove it
///     (ADR 0030): no active order, no recorded execution, not known by its exchange order ID, its trading day ended and settled,
///     no execution among the account's operations on the share, and a position equal to the engine's with nothing blocked. Any
///     other answer, or a read that fails, leaves the order unresolved with the check that stopped it. Values are invented; the
///     API is the in-process mock.
/// </summary>
public sealed class TInvestAbsenceProofTests
{
    private static readonly DateOnly Tuesday = new DateOnly(year: 2026, month: 10, day: 27);

    private static readonly string[] AllChecks =
    [
        "NotFoundByRequestId",
        "NotAmongActiveOrders",
        "NoExecutionRecorded",
        "NotFoundByExchangeId",
        "TradingDayEnded",
        "NoExecutionInOperations",
        "PositionMatchesEngine"
    ];

    private TInvestGatewayHarness _harness = null!;
    private ClientOrderId _client = null!;
    private OrderState _placed = null!;

    [SetUp]
    public async Task PlaceAnOrderOnTuesdayAndLetTheExchangeRemoveIt()
    {
        _harness = new TInvestGatewayHarness();
        _harness.Orders.Schedule.Add(Weekday(Tuesday));
        _client = _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId: 1);
        await _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot, AllowForeignOrders: false, []), CancellationToken.None);
        await _harness.Gateway.Submit(
            new OrderSubmitInternal(_harness.Bot, _client, TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m), MustLookUpBeforeSubmit: false),
            TInvestGatewayHarness.NoOp,
            CancellationToken.None);
        _placed = _harness.Orders.Order(_harness.Orders.Posted.Single().OrderId).Clone();
        _harness.Orders.Vanish(_placed.OrderRequestId);
    }

    [Test]
    public async Task EveryCheckHolding_TheOrderIsClosedAsAnOwnerCancelWithTheChecksThatJustifiedIt()
    {
        NextMorning();
        _harness.Orders.Operations.Add(Operation(OperationState.Canceled, quantityDone: 0));

        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBeNull(),
            () => reconciliation.Updates.ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 1)]),
            () => reconciliation.Found.ShouldHaveSingleItem().IsFinal.ShouldBeTrue(),
            () => reconciliation.Found.Single().AbsenceProof!.Checks.ShouldBe(AllChecks),
            () => _harness.Attempts.All.Single()
                .ShouldSatisfyAllConditions(
                    attempt => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
                    attempt => attempt.CancelledBy.ShouldBe(TInvestCancelReason.Owner),
                    attempt => attempt.ExecutedLots.ShouldBe(0)));
        _harness.Orders.Cancelled.ShouldBeEmpty();
        _harness.Orders.Posted.Count.ShouldBe(1);
    }

    [Test]
    public async Task TheOperationsAreReadForTheShareFromTheOrdersPlanningThroughNowWithoutCommissions()
    {
        NextMorning();
        await Reconcile(expectedBase: 0m);

        GetOperationsByCursorRequest request = _harness.Orders.OperationRequests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.AccountId.ShouldBe(TInvestGatewayHarness.AccountId),
            () => request.InstrumentId.ShouldBe(TInvestGatewayHarness.ShareUid),
            () => request.From.ToDateTimeOffset().ShouldBe(TInvestGatewayHarness.Now - TimeSpan.FromMinutes(5)),
            () => request.To.ToDateTimeOffset().ShouldBe(_harness.Clock.GetUtcNow()),
            () => request.Limit.ShouldBe(1000),
            () => request.WithoutCommissions.ShouldBeTrue(),
            () => request.WithoutTrades.ShouldBeTrue());
        _harness.Api.Calls.Count(call => call.Method.EndsWith("/GetPositions", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Test]
    public async Task WhileTheTradingDayHasNotEndedTheOrderStaysUnresolved()
    {
        At(Tuesday, hour: 12, minute: 0);

        await StaysUnresolved("TradingDayNotEnded");
    }

    [Test]
    public async Task TheDayMustAlsoHaveSettledForTheConfiguredDelay()
    {
        At(Tuesday, hour: 21, minute: 30);

        await StaysUnresolved("TradingDayNotEnded");

        At(Tuesday, hour: 21, minute: 50);

        (await Reconcile(expectedBase: 0m)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task ADayTheScheduleDoesNotShowIsUnknownNotEnded()
    {
        NextMorning();
        _harness.Orders.Schedule.Clear();

        await StaysUnresolved("TradingDayUnknown");
    }

    [Test]
    public async Task AnExecutionAmongTheOperationsKeepsItUnresolved_EvenAPartialOne()
    {
        NextMorning();
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOrderCancelledAfterAPartialExecutionKeepsItUnresolved()
    {
        NextMorning();
        _harness.Orders.Operations.Add(Operation(OperationState.Canceled, quantityDone: 1));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOperationInAStateTheModuleDoesNotKnowCountsAsAnExecution()
    {
        NextMorning();
        _harness.Orders.Operations.Add(Operation(OperationState.Unspecified, quantityDone: 0));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task OperationsBeyondTheLastPageThatMayBeReadAreNotAnEmptyAnswer()
    {
        NextMorning();
        _harness.Orders.OperationsPageSize = 1;

        for (var index = 0; index < 6; index++)
        {
            _harness.Orders.Operations.Add(Operation(OperationState.Canceled, quantityDone: 0));
        }

        await StaysUnresolved("OperationsIncomplete");
        _harness.Orders.OperationRequests.Count.ShouldBe(5);
    }

    [Test]
    public async Task SeveralPagesOfCancelledOrdersAreReadToTheEnd()
    {
        NextMorning();
        _harness.Orders.OperationsPageSize = 2;

        for (var index = 0; index < 5; index++)
        {
            _harness.Orders.Operations.Add(Operation(OperationState.Canceled, quantityDone: 0));
        }

        (await Reconcile(expectedBase: 0m)).Degradation.ShouldBeNull();
        _harness.Orders.OperationRequests.Count.ShouldBe(3);
    }

    [Test]
    public async Task AFailedOperationsReadIsAFailedCheck_NotAnEmptyAnswer()
    {
        NextMorning();
        _harness.Orders.OperationsFailure = MockTInvestApi.Refusal(StatusCode.Unavailable, "connection reset");

        await StaysUnresolved("ReadFailed");
    }

    [Test]
    public async Task APositionOtherThanTheEnginesKeepsItUnresolved()
    {
        NextMorning();
        _harness.Orders.PositionShares = 10;

        await StaysUnresolved("PositionDiffers");
    }

    [Test]
    public async Task APositionEqualToTheEnginesIsNotEnoughWhileAnythingIsBlocked()
    {
        NextMorning();
        _harness.Orders.PositionShares = 0;
        _harness.Orders.PositionBlockedShares = 10;

        await StaysUnresolved("PositionBlocked");
    }

    [Test]
    public async Task AFreshPositionOfSharesTheEngineExpectsIsTheSamePosition()
    {
        NextMorning();
        _harness.Orders.PositionShares = 25;

        (await Reconcile(expectedBase: 25m)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task APositionWhileTheBrokerStillLoadsLimitsCannotBeTrusted()
    {
        NextMorning();
        _harness.Orders.LimitsLoading = true;

        await StaysUnresolved("PositionUnreadable");
    }

    [Test]
    public async Task WithoutAnExpectedPositionNothingIsProven()
    {
        NextMorning();
        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: null);

        reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
        reconciliation.DegradationDetail.ShouldBe("AutoCheck:ExpectedPositionUnknown");
    }

    [Test]
    public async Task AnExecutionTheEngineBookedKeepsItUnresolved()
    {
        NextMorning();
        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m, executedBase: 10m);

        reconciliation.DegradationDetail.ShouldBe("AutoCheck:ExecutionRecorded");
    }

    [Test]
    public async Task AnOrderTInvestStillKnowsByItsExchangeIdIsNeverClosed()
    {
        NextMorning();
        _harness.Orders.KeepByExchangeId(_placed);

        await StaysUnresolved("KnownByExchangeId");
    }

    [Test]
    public async Task AnOrderThatHasLivedLongerThanTheWindowIsLeftToTheOwner()
    {
        At(Tuesday.AddDays(12), hour: 8, minute: 0);

        await StaysUnresolved("OrderTooOld");
    }

    [Test]
    public async Task TheOwnersConfirmationKeepsItsOwnShorterCheckAndNeedsNeitherOperationsNorPosition()
    {
        NextMorning();
        BotReconciliationInternal reconciliation = await _harness.Gateway.Reconcile(
            new BotReconcileInternal(
                _harness.Bot,
                AllowForeignOrders: false,
                [new ReconcileOrderInternal(EngineOrderId: 1, _client, MayHaveReachedVenue: true, OwnerConfirmedAbsent: true)]),
            CancellationToken.None);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBeNull(),
            () => reconciliation.Found.Single().AbsenceProof.ShouldBeNull());
        _harness.Orders.OperationRequests.ShouldBeEmpty();
    }

    [Test]
    public async Task TheDryCheckAnswersWithoutMovingAnythingOrOpeningTheStream()
    {
        NextMorning();
        await _harness.Streams.Close(_harness.Bot);

        bool provable = await _harness.Gateway.CanProveAbsence(Request(expectedBase: 0m), CancellationToken.None);

        provable.ShouldBeTrue();
        _harness.Attempts.All.Single().State.ShouldBe(TInvestOrderAttemptState.Active);
        _harness.Streams.IsOpen(_harness.Bot).ShouldBeFalse();
        _harness.Orders.Cancelled.ShouldBeEmpty();
    }

    [Test]
    public async Task TheDryCheckIsFalseForAnOrderItCannotProveAndForAnOrderTInvestStillKnows()
    {
        At(Tuesday, hour: 12, minute: 0);

        (await _harness.Gateway.CanProveAbsence(Request(expectedBase: 0m), CancellationToken.None)).ShouldBeFalse();

        NextMorning();
        _harness.Orders.PositionShares = 3;

        (await _harness.Gateway.CanProveAbsence(Request(expectedBase: 0m), CancellationToken.None)).ShouldBeFalse();
    }

    [Test]
    public async Task TheDryCheckIsFalseWhenNothingNeedsAProof()
    {
        NextMorning();
        (await _harness.Gateway.CanProveAbsence(new BotReconcileInternal(_harness.Bot, AllowForeignOrders: false, [], ExpectedBase: 0m), CancellationToken.None))
            .ShouldBeFalse();
    }

    private async Task StaysUnresolved(string failedCheck)
    {
        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => reconciliation.DegradationDetail.ShouldBe($"AutoCheck:{failedCheck}"),
            () => reconciliation.Updates.ShouldBeEmpty(),
            () => reconciliation.Found.ShouldBeEmpty(),
            () => _harness.Attempts.All.Single().State.ShouldBe(TInvestOrderAttemptState.Active));
    }

    private Task<BotReconciliationInternal> Reconcile(decimal? expectedBase, decimal executedBase = 0m)
    {
        return _harness.Gateway.Reconcile(Request(expectedBase, executedBase), CancellationToken.None);
    }

    private BotReconcileInternal Request(decimal? expectedBase, decimal executedBase = 0m)
    {
        return new BotReconcileInternal(
            _harness.Bot,
            AllowForeignOrders: false,
            [new ReconcileOrderInternal(EngineOrderId: 1, _client, MayHaveReachedVenue: true, OwnerConfirmedAbsent: false, executedBase, TInvestGatewayHarness.Now)],
            expectedBase);
    }

    private void NextMorning()
    {
        At(Tuesday.AddDays(1), hour: 8, minute: 0);
    }

    private void At(DateOnly date, int hour, int minute)
    {
        _harness.Clock.SetUtcNow(new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero));
    }

    private static OperationItem Operation(OperationState state, long quantityDone)
    {
        return new OperationItem
        {
            Id = Guid.NewGuid().ToString("N"),
            InstrumentUid = TInvestGatewayHarness.ShareUid,
            State = state,
            Type = OperationType.Buy,
            QuantityDone = quantityDone,
            Date = Timestamp.FromDateTimeOffset(TInvestGatewayHarness.Now.AddMinutes(1))
        };
    }

    private static (DateOnly Date, List<(string Type, DateTimeOffset From, DateTimeOffset To)> Intervals) Weekday(DateOnly date)
    {
        return (date,
        [
            ("opening_auction_morning", Utc(date, hour: 3, minute: 50), Utc(date, hour: 3, minute: 59)),
            ("opening_auction", Utc(date, hour: 3, minute: 50), Utc(date, hour: 3, minute: 59)),
            ("regular_trading_session_morning", Utc(date, hour: 4, minute: 0), Utc(date, hour: 5, minute: 59)),
            ("regular_trading_session_main", Utc(date, hour: 6, minute: 0), Utc(date, hour: 15, minute: 54)),
            ("regular_trading_session", Utc(date, hour: 6, minute: 0), Utc(date, hour: 15, minute: 54)),
            ("closing_auction_main", Utc(date, hour: 15, minute: 55), Utc(date, hour: 15, minute: 59)),
            ("closing_auction", Utc(date, hour: 15, minute: 55), Utc(date, hour: 15, minute: 59)),
            ("regular_trading_session_evening", Utc(date, hour: 16, minute: 0), Utc(date, hour: 20, minute: 49))
        ]);
    }

    private static DateTimeOffset Utc(DateOnly date, int hour, int minute)
    {
        return new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);
    }
}
