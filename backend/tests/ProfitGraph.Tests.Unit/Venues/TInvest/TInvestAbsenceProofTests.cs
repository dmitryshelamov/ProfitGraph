using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Trading;

using Tinkoff.InvestApi.V1;

using EngineOrderType = ProfitGraph.Engine.Accounting.OrderType;

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

    private static readonly string[] CalendarChecks =
    [
        "NotFoundByRequestId",
        "NotAmongActiveOrders",
        "NoExecutionRecorded",
        "NotFoundByExchangeId",
        "TradingDayEnded",
        "TradingDayEndedByCalendar",
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
        await PlaceAndVanish();
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
            () => request.From.ToDateTimeOffset().ShouldBe(TInvestGatewayHarness.Now - TimeSpan.FromSeconds(15)),
            () => request.To.ToDateTimeOffset().ShouldBe(_harness.Clock.GetUtcNow()),
            () => request.Limit.ShouldBe(1000),
            () => request.WithoutCommissions.ShouldBeTrue(),
            () => request.WithoutTrades.ShouldBeFalse());
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
    public async Task APurchaseOfTheBotDatedBeforeTheOrderWasPlannedIsNotItsExecution()
    {
        NextMorning();
        _harness.Orders.FilterOperationsByRange = true;
        _harness.Orders.Operations.Add(OperationAt(TInvestGatewayHarness.Now - TimeSpan.FromSeconds(32), OperationState.Executed, quantityDone: 2));
        _harness.Orders.Operations.Add(OperationAt(TInvestGatewayHarness.Now.AddMinutes(1), OperationState.Canceled, quantityDone: 0));

        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBeNull(),
            () => reconciliation.Updates.ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 1)]),
            () => reconciliation.Found.Single().AbsenceProof!.Checks.ShouldBe(AllChecks));
    }

    [Test]
    public async Task AnExecutionDatedWithinTheClockMarginBeforeThePlanningStillBlocks()
    {
        NextMorning();
        _harness.Orders.FilterOperationsByRange = true;
        _harness.Orders.Operations.Add(OperationAt(TInvestGatewayHarness.Now - TimeSpan.FromSeconds(10), OperationState.Executed, quantityDone: 1));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnExecutionDatedAfterThePlanningStillBlocks()
    {
        NextMorning();
        _harness.Orders.FilterOperationsByRange = true;
        _harness.Orders.Operations.Add(OperationAt(TInvestGatewayHarness.Now.AddSeconds(2), OperationState.Executed, quantityDone: 1));

        await StaysUnresolved("ExecutionInOperations");
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
    public async Task AnEarlierPurchaseOfTheBotInTheWindowIsNotAnExecutionOfTheOrder_AndTheFeedNamesIt()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 2, "trade-2a", "trade-2b");
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 2, Trade("trade-2a", quantity: 1), Trade("trade-2b", quantity: 1)));
        _harness.Orders.Operations.Add(Operation(OperationState.Canceled, quantityDone: 0));

        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBeNull(),
            () => reconciliation.Updates.ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 1)]),
            () => reconciliation.Found.Single().AbsenceProof!.Checks.ShouldBe([.. AllChecks[..^1], "OwnExecutionsExcluded:1:2", AllChecks[^1]]));
        _harness.Orders.Posted.Count.ShouldBe(1);
    }

    [Test]
    public async Task OperationsOfSeveralOtherOrdersAreCountedPerOperationAndNamedByOrder()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        AddExecutedOrder(engineOrderId: 3, lots: 1, "trade-3");
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade("trade-3", quantity: 1)));
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade("trade-2", quantity: 1)));

        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.Found.Single().AbsenceProof!.Checks.ShouldContain("OwnExecutionsExcluded:2:2+3");
    }

    [Test]
    public async Task AnOperationOfUnknownOriginAmongTheBotsOwnKeepsItUnresolved()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade("trade-2", quantity: 1)));
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade("trade-of-someone-else", quantity: 1)));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOperationWhoseTradesAreOnlyPartlyKnownKeepsItUnresolved()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 2, Trade("trade-2", quantity: 1), Trade("trade-unknown", quantity: 1)));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnExecutionOfTheCheckedOrderItselfKeepsItUnresolved()
    {
        NextMorning();
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade("trade-1", quantity: 1)));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOtherOrdersOperationStillExecutingKeepsItUnresolved()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        _harness.Orders.Operations.Add(Operation(OperationState.Progress, quantityDone: 1, Trade("trade-2", quantity: 1)));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOperationWhoseTradesDoNotAddUpToItsExecutedQuantityKeepsItUnresolved()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 3, Trade("trade-2", quantity: 1)));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOperationWithoutATradeNumberKeepsItUnresolved()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1));

        await StaysUnresolved("ExecutionInOperations");

        _harness.Orders.Operations.Clear();
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade(string.Empty, quantity: 1)));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOtherOperationTypeOnTheShareKeepsItUnresolved_EvenWithAKnownTradeNumber()
    {
        NextMorning();
        AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        OperationItem fee = Operation(OperationState.Executed, quantityDone: 1, Trade("trade-2", quantity: 1));
        fee.Type = OperationType.BrokerFee;
        _harness.Orders.Operations.Add(fee);

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOtherOrderThatT_InvestNoLongerAnswersForExplainsNothing()
    {
        NextMorning();
        TInvestOrderAttempt other = AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        _harness.Orders.Forget(other.ExchangeOrderId!);
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade("trade-2", quantity: 1)));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AnOtherOrderWhoseStagesDisagreeWithTheBookedLotsExplainsNothing()
    {
        NextMorning();
        TInvestOrderAttempt other = AddExecutedOrder(engineOrderId: 2, lots: 1, "trade-2");
        (await _harness.Attempts.Transition(other with { ExecutedLots = 5 }, TInvestOrderAttemptState.Final, CancellationToken.None)).ShouldBeTrue();
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1, Trade("trade-2", quantity: 1)));

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

    [Test]
    public async Task WhenThePastScheduleIsRefusedAMoexOrderOfYesterdayInMoscowIsProvenEndedByTheCalendar()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        NextMorning();

        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBeNull(),
            () => reconciliation.Updates.ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 1)]),
            () => reconciliation.Found.Single().AbsenceProof!.Checks.ShouldBe(CalendarChecks));
    }

    [Test]
    public async Task ATimedOutScheduleFallsBackToTheCalendarToo()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.DeadlineExceeded, "timeout");
        NextMorning();

        (await Reconcile(expectedBase: 0m)).Found.Single().AbsenceProof!.Checks.ShouldBe(CalendarChecks);
    }

    [Test]
    public async Task TheCalendarDayEndsAtMoscowMidnightPlusTheSettleDelay()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");

        At(Tuesday, hour: 21, minute: 59);

        await StaysUnresolved("TradingDayNotEnded");

        At(Tuesday, hour: 22, minute: 0);

        (await Reconcile(expectedBase: 0m)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task ADayThatIsStillRunningIsNotEndedByTheCalendar()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        At(Tuesday, hour: 12, minute: 0);

        await StaysUnresolved("TradingDayNotEnded");
    }

    [Test]
    public async Task AnOrderPlacedInTheEveningJustBeforeMoscowMidnightEndsWithThatMoscowDay()
    {
        _harness = new TInvestGatewayHarness();
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        At(Tuesday, hour: 20, minute: 58);
        await PlaceAndVanish();

        At(Tuesday, hour: 21, minute: 59);

        await StaysUnresolved("TradingDayNotEnded");

        At(Tuesday, hour: 22, minute: 0);

        (await Reconcile(expectedBase: 0m)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task AnOrderLastRecordedAfterMoscowMidnightBelongsToTheNextMoscowDay()
    {
        _harness = new TInvestGatewayHarness();
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        At(Tuesday, hour: 21, minute: 2);
        await PlaceAndVanish();

        At(Tuesday.AddDays(1), hour: 21, minute: 59);

        await StaysUnresolved("TradingDayNotEnded");

        At(Tuesday.AddDays(1), hour: 22, minute: 0);

        (await Reconcile(expectedBase: 0m)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task AnExchangeOutsideTheMoexWhitelistIsNeverEndedByTheCalendar()
    {
        _harness = new TInvestGatewayHarness(exchange: "spb_main");
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        await PlaceAndVanish();
        NextMorning();

        await StaysUnresolved("TradingDayUnknown");
    }

    [Test]
    public async Task AClockBehindTheOrdersLastRecordedInstantProvesNothing()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        TInvestOrderAttempt recorded = _harness.Attempts.All.Single();
        (await _harness.Attempts.Transition(recorded with { UpdatedAt = recorded.UpdatedAt.AddHours(2) }, recorded.State, CancellationToken.None)).ShouldBeTrue();

        await StaysUnresolved("TradingDayUnknown");
    }

    [Test]
    public async Task TheCalendarDoesNotReplaceTheOtherChecks()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        NextMorning();
        _harness.Orders.Operations.Add(Operation(OperationState.Executed, quantityDone: 1));

        await StaysUnresolved("ExecutionInOperations");
    }

    [Test]
    public async Task AReadableScheduleDecidesAndTheCalendarIsNotUsed()
    {
        At(Tuesday, hour: 21, minute: 50);

        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.Found.Single().AbsenceProof!.Checks.ShouldBe(AllChecks);
    }

    private async Task PlaceAndVanish()
    {
        _client = _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId: 1);
        await _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot, AllowForeignOrders: false, []), CancellationToken.None);
        await _harness.Gateway.Submit(
            new OrderSubmitInternal(_harness.Bot, _client, TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 99m), MustLookUpBeforeSubmit: false),
            TInvestGatewayHarness.NoOp,
            CancellationToken.None);
        _placed = _harness.Orders.Order(_harness.Orders.Posted.Single().OrderId).Clone();
        _harness.Orders.Vanish(_placed.OrderRequestId);
    }

    private async Task StaysUnresolved(string failedCheck)
    {
        BotReconciliationInternal reconciliation = await Reconcile(expectedBase: 0m);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => reconciliation.DegradationDetail.ShouldBe($"AutoCheck:{failedCheck}"),
            () => reconciliation.Updates.ShouldBeEmpty(),
            () => reconciliation.Found.ShouldBeEmpty(),
            () => _harness.Attempts.All.Single(row => row.EngineOrderId == 1).State.ShouldBe(TInvestOrderAttemptState.Active));
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

    /// <summary>
    ///     A final attempt of another engine order of the bot that booked lots, with the exchange order T-Invest still answers
    ///     for and its trade stages (the lots split over the trades; one lot a trade when there are several).
    /// </summary>
    private TInvestOrderAttempt AddExecutedOrder(long engineOrderId, long lots, string firstTrade, params string[] otherTrades)
    {
        string[] trades = [firstTrade, .. otherTrades];
        var exchangeOrderId = $"8590{engineOrderId}1";
        var requestId = Guid.NewGuid();
        var state = new OrderState
        {
            OrderId = exchangeOrderId,
            OrderRequestId = requestId.ToString("D"),
            LotsRequested = lots,
            LotsExecuted = lots,
            ExecutionReportStatus = OrderExecutionReportStatus.ExecutionReportStatusFill
        };

        for (var index = 0; index < trades.Length; index++)
        {
            state.Stages.Add(new OrderStage { TradeId = trades[index], Quantity = trades.Length == 1 ? lots : lots / trades.Length });
        }

        _harness.Orders.KeepByExchangeId(state);

        var added = new TInvestOrderAttempt(
            _harness.Bot.Value,
            engineOrderId,
            Attempt: 1,
            requestId,
            TInvestGatewayHarness.ShareUid,
            OrderSide.Buy,
            EngineOrderType.Market,
            lots,
            Price: null,
            TInvestOrderAttemptState.Final,
            exchangeOrderId,
            BrokerCode: null,
            TInvestGatewayHarness.Now,
            TInvestGatewayHarness.Now,
            ExecutedLots: lots,
            ExecutedQuote: 100m * lots,
            ExecutedFee: 1m);

        _harness.Attempts.Add(added, CancellationToken.None).Result.ShouldBeTrue();

        return added;
    }

    private static OperationItemTrade Trade(string number, long quantity)
    {
        return new OperationItemTrade { Num = number, Quantity = quantity };
    }

    private static OperationItem Operation(OperationState state, long quantityDone, params OperationItemTrade[] trades)
    {
        return OperationAt(TInvestGatewayHarness.Now.AddMinutes(1), state, quantityDone, trades);
    }

    private static OperationItem OperationAt(DateTimeOffset at, OperationState state, long quantityDone, params OperationItemTrade[] trades)
    {
        var item = new OperationItem
        {
            Id = Guid.NewGuid().ToString("N"),
            InstrumentUid = TInvestGatewayHarness.ShareUid,
            State = state,
            Type = OperationType.Buy,
            QuantityDone = quantityDone,
            Date = Timestamp.FromDateTimeOffset(at)
        };

        if (trades.Length > 0)
        {
            item.TradesInfo = new OperationItemTrades();
            item.TradesInfo.Trades.AddRange(trades);
        }

        return item;
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
