using Grpc.Core;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     A DAY order the exchange ended with its trading day (ADR 0031): T-Invest's own cancellation, read once the day has ended
///     by the schedule, ends the engine order with what it executed, while the account's position covers what its bots hold;
///     a cancellation nobody asked for during the session, or on a day the schedule does not know, stays unresolved, with its
///     outcome recorded. The post-session sweep records the outcome of orders no runner reads, only by reading, and tries a
///     failed read again sooner first. The calendar is the observed weekday of <c>moex_mrng_evng_e_wknd_dlr</c> in UTC (the
///     day ends at 20:49); values are invented and the API is the in-process mock.
/// </summary>
public sealed class TInvestDayEndTests
{
    private static readonly DateOnly Tuesday = new DateOnly(year: 2026, month: 10, day: 27);

    private static readonly string[] ExpiryChecks = ["CancelledByExchange", "TradingDayEnded", "PositionCoversBots"];

    private TInvestGatewayHarness _harness = null!;

    [SetUp]
    public void CreateGateway()
    {
        _harness = new TInvestGatewayHarness();
        _harness.Orders.Schedule.Add(Weekday(Tuesday));
        _harness.Orders.Schedule.Add(Weekday(Tuesday.AddDays(1)));
    }

    [Test]
    public async Task AnOrderTheExchangeEndsWithItsDayIsCancelledInTheEngine_WithNothingExecuted()
    {
        string requestId = await PlaceBuy(lots: 1);

        At(Tuesday, hour: 20, minute: 50);
        _harness.Orders.Expire(requestId);

        IReadOnlyList<ExternalOrderUpdate> updates = await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed);
        updates.ShouldNotContain(update => update.Kind == ExternalOrderUpdateKind.PartialFill);
        Attempt()
            .ShouldSatisfyAllConditions(
                attempt => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
                attempt => attempt.CancelledBy.ShouldBe(TInvestCancelReason.Expired),
                attempt => attempt.ExecutedLots.ShouldBe(0));
        _harness.Orders.Cancelled.ShouldBeEmpty();
    }

    [Test]
    public async Task AnOrderTheExchangeEndsAfterAPartialFillKeepsTheFill_AndOnlyTheRemainderIsCancelled()
    {
        string requestId = await PlaceBuy(lots: 3);
        _harness.Orders.Fill(requestId, lots: 1, price: 99.5m, commission: 0.5m);
        _harness.Orders.PositionShares = 10;

        At(Tuesday, hour: 20, minute: 50);
        _harness.Orders.Expire(requestId);

        IReadOnlyList<ExternalOrderUpdate> updates = await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed);
        updates.ShouldContain(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 10m, grossQuote: 995m, fee: 0.5m, commissionPartitioned: true));
        updates[^1].ShouldBe(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        Attempt()
            .ShouldSatisfyAllConditions(
                attempt => attempt.CancelledBy.ShouldBe(TInvestCancelReason.Expired),
                attempt => attempt.ExecutedLots.ShouldBe(1),
                attempt => attempt.ExecutedQuote.ShouldBe(995m));
    }

    [Test]
    public async Task ACancellationNobodyAskedForDuringTheSessionIsTheOwnersCancel_WithItsExecutionsBooked()
    {
        string requestId = await PlaceBuy(lots: 3);
        _harness.Orders.Fill(requestId, lots: 1, price: 99.5m, commission: 0.5m);
        _harness.Orders.PositionShares = 10;

        At(Tuesday, hour: 12, minute: 0);
        _harness.Orders.Expire(requestId);

        IReadOnlyList<ExternalOrderUpdate> updates = await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed);

        updates[^1].ShouldBe(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        Attempt()
            .ShouldSatisfyAllConditions(
                attempt => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
                attempt => attempt.CancelledBy.ShouldBe(TInvestCancelReason.Owner),
                attempt => attempt.ExecutedLots.ShouldBe(1),
                attempt => attempt.ExecutedQuote.ShouldBe(995m),
                attempt => attempt.ExecutedFee.ShouldBe(0.5m));
    }

    [Test]
    public async Task OnADayTheScheduleDoesNotKnowACancellationNobodyAskedForIsTheOwnersCancel()
    {
        _harness.Orders.Schedule.Clear();
        (DateOnly Date, List<(string Type, DateTimeOffset From, DateTimeOffset To)> Intervals) day = Weekday(Tuesday);
        day.Intervals.Add(("regular_trading_session_night", Utc(Tuesday, hour: 21, minute: 0), Utc(Tuesday, hour: 22, minute: 0)));
        _harness.Orders.Schedule.Add(day);
        string requestId = await PlaceBuy(lots: 1);

        At(Tuesday, hour: 22, minute: 30);
        _harness.Orders.Expire(requestId);

        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed))[^1].ShouldBe(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        Attempt().CancelledBy.ShouldBe(TInvestCancelReason.Owner);
    }

    [Test]
    public async Task APositionThatDoesNotCoverTheBotsLeavesTheExpiredOrderToTheOwner_UntilItDoes()
    {
        string requestId = await PlaceBuy(lots: 1);
        _harness.Positions.Hold(20m);
        _harness.Orders.PositionShares = 10;

        At(Tuesday, hour: 20, minute: 50);
        _harness.Orders.Expire(requestId);

        var failure = await Should.ThrowAsync<OrderGatewaySafetyException>(() => _harness.Until(_ => false));
        failure.ShouldSatisfyAllConditions(
            () => failure.Reason.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => failure.Detail.ShouldBe("AutoCheck:PositionShort"));
        Attempt().CancelledBy.ShouldBe(TInvestCancelReason.Expired);
        BotReconciliationInternal stopped = await Reconcile(executedBase: 0m);
        stopped.ShouldSatisfyAllConditions(
            () => stopped.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => stopped.DegradationDetail.ShouldBe("AutoCheck:PositionShort"));
        (await CanProve(executedBase: 0m)).ShouldBeFalse();

        // The owner holds shares of their own beside the bot's: coverage is not equality.
        _harness.Orders.PositionShares = 25;

        (await CanProve(executedBase: 0m)).ShouldBeTrue();
        BotReconciliationInternal resumed = await Reconcile(executedBase: 0m);
        resumed.ShouldSatisfyAllConditions(
            () => resumed.Degradation.ShouldBeNull(),
            () => resumed.Updates.ShouldBe([ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.ConfirmCancel(orderId: 1)]),
            () => resumed.Found.ShouldHaveSingleItem().AbsenceProof!.Checks.ShouldBe(ExpiryChecks));
    }

    [Test]
    public async Task EveryBotOnTheAccountAndShareCounts_NotOnlyTheOneWhoseOrderEnded()
    {
        string requestId = await PlaceBuy(lots: 1);
        _harness.Positions.Bots.Add(new VenueBotPositionInternal(Guid.NewGuid(), "TQBR:SBER", BaseTotal: 30m));
        _harness.Orders.PositionShares = 20;
        await _harness.Streams.Close(_harness.Bot);

        At(Tuesday, hour: 20, minute: 50);
        _harness.Orders.Expire(requestId);

        (await Reconcile(executedBase: 0m)).DegradationDetail.ShouldBe("AutoCheck:PositionShort");
        _harness.Orders.PositionShares = 30;
        (await Reconcile(executedBase: 0m)).Degradation.ShouldBeNull();
    }

    [TestCase("bots", "AutoCheck:BotPositionsUnknown")]
    [TestCase("read", "AutoCheck:ReadFailed")]
    [TestCase("loading", "AutoCheck:PositionUnreadable")]
    public async Task AnUnknownPositionNeverCovers(string cause, string detail)
    {
        string requestId = await PlaceBuy(lots: 1);
        await _harness.Streams.Close(_harness.Bot);

        switch (cause)
        {
            case "bots":
                _harness.Positions.Failure = new InvalidOperationException("Invented.");

                break;
            case "read":
                _harness.Orders.PositionsFailure = MockTInvestApi.Refusal(StatusCode.Unavailable, "70001");

                break;
            default:
                _harness.Orders.LimitsLoading = true;

                break;
        }

        At(Tuesday, hour: 20, minute: 50);
        _harness.Orders.Expire(requestId);

        BotReconciliationInternal reconciliation = await Reconcile(executedBase: 0m);
        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => reconciliation.DegradationDetail.ShouldBe(detail),
            () => reconciliation.Updates.ShouldNotContain(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed));
    }

    [Test]
    public async Task TheSweepRecordsTheOutcomeOfAnOrderNobodyReads_AndTheNextStartAcceptsItWhenTInvestHasForgottenIt()
    {
        string requestId = await PlaceBuy(lots: 3);
        _harness.Orders.Fill(requestId, lots: 1, price: 99.5m, commission: 0.5m);
        await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.PartialFill);
        await _harness.Streams.Close(_harness.Bot);
        _harness.Positions.Hold(10m);
        _harness.Orders.PositionShares = 10;

        At(Tuesday, hour: 20, minute: 50);
        _harness.Orders.Expire(requestId);
        await _harness.Sweep.Round(CancellationToken.None);

        Attempt()
            .ShouldSatisfyAllConditions(
                attempt => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
                attempt => attempt.CancelledBy.ShouldBe(TInvestCancelReason.Expired),
                attempt => attempt.ExecutedLots.ShouldBe(1));
        _harness.Orders.Posted.Count.ShouldBe(1);
        _harness.Orders.Cancelled.ShouldBeEmpty();

        _harness.Orders.Vanish(requestId);
        At(Tuesday.AddDays(1), hour: 8, minute: 0);
        BotReconciliationInternal reconciliation = await Reconcile(executedBase: 10m);

        reconciliation.ShouldSatisfyAllConditions(
            () => reconciliation.Degradation.ShouldBeNull(),
            () => reconciliation.Updates.ShouldBe(
            [
                ExternalOrderUpdate.Accept(orderId: 1),
                ExternalOrderUpdate.Partial(orderId: 1, grossBase: 10m, grossQuote: 995m, fee: 0.5m, commissionPartitioned: true),
                ExternalOrderUpdate.ConfirmCancel(orderId: 1)
            ]),
            () => reconciliation.Found.ShouldHaveSingleItem().IsFinal.ShouldBeTrue(),
            () => reconciliation.Found.Single().AbsenceProof!.Checks.ShouldBe(ExpiryChecks));
        _harness.Orders.Posted.Count.ShouldBe(1);
        _harness.Orders.Cancelled.ShouldBeEmpty();
    }

    [Test]
    public async Task ABotStoppedOnAnOrderTheSweepRecordedAsExpiredIsCheckedAgainSuccessfully()
    {
        string requestId = await PlaceBuy(lots: 1);
        await _harness.Streams.Close(_harness.Bot);

        At(Tuesday, hour: 20, minute: 50);
        _harness.Orders.Expire(requestId);
        await _harness.Sweep.Round(CancellationToken.None);
        _harness.Orders.Vanish(requestId);
        At(Tuesday.AddDays(1), hour: 8, minute: 0);

        (await CanProve(executedBase: 0m)).ShouldBeTrue();
        _harness.Orders.StateReads.ShouldBeGreaterThan(0);
        Attempt().State.ShouldBe(TInvestOrderAttemptState.Final);
    }

    [Test]
    public async Task BeforeTheDayEndsTheSweepReadsNothing_AndComesBackAtTheDaysEnd()
    {
        await PlaceBuy(lots: 1);
        await _harness.Streams.Close(_harness.Bot);
        int reads = _harness.Orders.StateReads;

        At(Tuesday, hour: 20, minute: 40);
        DateTimeOffset next = await _harness.Sweep.Round(CancellationToken.None);

        next.ShouldBe(Utc(Tuesday, hour: 20, minute: 49));
        _harness.Orders.StateReads.ShouldBe(reads);
    }

    [Test]
    public async Task OnADayTheScheduleDoesNotKnowTheSweepReadsNothing()
    {
        _harness.Orders.Schedule.Clear();
        (DateOnly Date, List<(string Type, DateTimeOffset From, DateTimeOffset To)> Intervals) day = Weekday(Tuesday);
        day.Intervals.Add(("regular_trading_session_night", Utc(Tuesday, hour: 21, minute: 0), Utc(Tuesday, hour: 22, minute: 0)));
        _harness.Orders.Schedule.Add(day);
        await PlaceBuy(lots: 1);
        await _harness.Streams.Close(_harness.Bot);
        int reads = _harness.Orders.StateReads;

        At(Tuesday, hour: 22, minute: 30);
        await _harness.Sweep.Round(CancellationToken.None);

        _harness.Orders.StateReads.ShouldBe(reads);
        Attempt().State.ShouldBe(TInvestOrderAttemptState.Active);
    }

    [Test]
    public async Task TheSweepLeavesARunningBotToItsStream_AndStopsOnceTheNextSessionOpens()
    {
        await PlaceBuy(lots: 1);

        // The submit's acceptance, then the stream's, published once it has read the new order.
        (await _harness.Next(count: 2)).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.Accept(orderId: 1)]);
        int reads = _harness.Orders.StateReads;

        At(Tuesday, hour: 20, minute: 50);
        await _harness.Sweep.Round(CancellationToken.None);
        _harness.Orders.StateReads.ShouldBe(reads);

        await _harness.Streams.Close(_harness.Bot);
        At(Tuesday.AddDays(1), hour: 4, minute: 0);
        await _harness.Sweep.Round(CancellationToken.None);
        _harness.Orders.StateReads.ShouldBe(reads);
    }

    [Test]
    public async Task AFailedReadIsTriedAgainAfterFiveSecondsDoublingToAMinute_ThenAtTheNextScheduledSweep()
    {
        string requestId = await PlaceBuy(lots: 1);
        await _harness.Streams.Close(_harness.Bot);
        _harness.Orders.Expire(requestId);
        _harness.Orders.StateFailures = int.MaxValue;
        At(Tuesday, hour: 20, minute: 50);
        List<TimeSpan> pauses = [];

        for (var round = 0; round < 7; round++)
        {
            DateTimeOffset now = _harness.Clock.GetUtcNow();
            DateTimeOffset next = await _harness.Sweep.Round(CancellationToken.None);
            pauses.Add(next - now);

            // Not due yet: nothing is read.
            int reads = _harness.Orders.StateReads;
            await _harness.Sweep.Round(CancellationToken.None);
            _harness.Orders.StateReads.ShouldBe(reads);

            _harness.Clock.SetUtcNow(next);
        }

        pauses.ShouldBe(
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5)
        ]);
        Attempt().State.ShouldBe(TInvestOrderAttemptState.Active);

        _harness.Orders.StateFailures = 0;
        await _harness.Sweep.Round(CancellationToken.None);

        Attempt().CancelledBy.ShouldBe(TInvestCancelReason.Expired);
    }

    [Test]
    public async Task ACancellationNobodyAskedForIsTheOwnersCancelEvenWhenTheScheduleCannotBeRead()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        string requestId = await PlaceBuy(lots: 1);

        At(Tuesday, hour: 12, minute: 0);
        _harness.Orders.Expire(requestId);

        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed))[^1].ShouldBe(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        Attempt().CancelledBy.ShouldBe(TInvestCancelReason.Owner);
    }

    [Test]
    public async Task WhenTheScheduleCannotBeReadTheMoscowCalendarEndsTheDayOfTheCancelledOrder()
    {
        _harness.Orders.ScheduleFailure = MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30003");
        string requestId = await PlaceBuy(lots: 1);

        At(Tuesday.AddDays(1), hour: 0, minute: 30);
        _harness.Orders.Expire(requestId);

        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed))[^1].ShouldBe(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        Attempt().CancelledBy.ShouldBe(TInvestCancelReason.Expired);
    }

    [Test]
    public async Task ACancellationNobodyAskedForIsStillClosedWhenTInvestHasForgottenTheOrderAtTheNextStart()
    {
        string requestId = await PlaceBuy(lots: 1);
        At(Tuesday, hour: 12, minute: 0);
        _harness.Orders.Expire(requestId);
        await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed);
        _harness.Orders.Vanish(requestId);
        At(Tuesday.AddDays(1), hour: 8, minute: 0);

        BotReconciliationInternal reconciliation = await Reconcile(executedBase: 0m);

        reconciliation.Degradation.ShouldBeNull();
        Attempt().CancelledBy.ShouldBe(TInvestCancelReason.Owner);
    }

    private async Task<string> PlaceBuy(long lots)
    {
        await _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot, AllowForeignOrders: false, []), CancellationToken.None);
        await _harness.Gateway.Submit(
            new OrderSubmitInternal(
                _harness.Bot,
                Client(),
                TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, lots * 10m, price: 100m),
                MustLookUpBeforeSubmit: false),
            TInvestGatewayHarness.NoOp,
            CancellationToken.None);

        return _harness.Orders.Posted.Single().OrderId;
    }

    private TInvestOrderAttempt Attempt()
    {
        return _harness.Attempts.All.ShouldHaveSingleItem();
    }

    private ClientOrderId Client()
    {
        return _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId: 1);
    }

    private BotReconcileInternal Request(decimal executedBase, bool ownerConfirmedAbsent = false)
    {
        return new BotReconcileInternal(
            _harness.Bot,
            AllowForeignOrders: false,
            [new ReconcileOrderInternal(EngineOrderId: 1, Client(), MayHaveReachedVenue: true, ownerConfirmedAbsent, executedBase, TInvestGatewayHarness.Now)]);
    }

    private Task<BotReconciliationInternal> Reconcile(decimal executedBase, bool ownerConfirmedAbsent = false)
    {
        return _harness.Gateway.Reconcile(Request(executedBase, ownerConfirmedAbsent), CancellationToken.None);
    }

    private Task<bool> CanProve(decimal executedBase)
    {
        return _harness.Gateway.CanProveAbsence(Request(executedBase), CancellationToken.None);
    }

    private void At(DateOnly date, int hour, int minute)
    {
        _harness.Clock.SetUtcNow(Utc(date, hour, minute));
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