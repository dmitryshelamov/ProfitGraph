using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Trading;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     DAY orders over the end of their trading day (ADR 0023, T12b; Profit Fox Designer ADR 0075 and 0090): shortly before
///     the day's last exchange interval ends the gateway cancels its working DAY orders, keeps each engine order working and
///     places only the whole-lot remainder on the next trading date, with fills cumulative across attempts; a cancellation
///     nobody asked for is unresolved; holiday intervals count as closed; a day with an unknown token plans nothing. The
///     calendar is the observed weekday of <c>moex_mrng_evng_e_wknd_dlr</c>, in UTC; values are invented.
/// </summary>
public sealed class TInvestCarryOverTests
{
    private static readonly DateOnly Tuesday = new DateOnly(year: 2026, month: 10, day: 27);

    private TInvestGatewayHarness _harness = null!;

    [SetUp]
    public void CreateGateway()
    {
        _harness = new TInvestGatewayHarness();
    }

    [Test]
    public async Task ADayOrderIsCarriedOverBeforeTheDayEnds_AndOnlyItsRemainderLeavesOnTheNextTradingDate()
    {
        _harness.Orders.Schedule.Add(Weekday(Tuesday));
        _harness.Orders.Schedule.Add(Weekday(Tuesday.AddDays(1)));
        await Reconcile();
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 30m, price: 100m));
        string first = _harness.Orders.Posted.Single().OrderId;
        _harness.Orders.Fill(first, lots: 1, price: 99.5m, commission: 0.5m);
        await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.PartialFill);

        At(Tuesday, hour: 20, minute: 44, second: 30);
        await _harness.Worker.Round(CancellationToken.None);

        _harness.Orders.Cancelled.ShouldHaveSingleItem().OrderId.ShouldBe(first);
        _harness.Attempts.All.Select(attempt => (attempt.Attempt, attempt.State, attempt.Lots, attempt.WaitingCode, attempt.CarriedFrom))
            .ShouldBe(
            [
                (1, TInvestOrderAttemptState.Final, 3L, null, Tuesday),
                (2, TInvestOrderAttemptState.Planned, 2L, (OrderWaitingCode?)OrderWaitingCode.CarryOver, (DateOnly?)Tuesday)
            ]);
        _harness.Waits.Reports[^1].ShouldBe((1L, (OrderWaitingCode?)OrderWaitingCode.CarryOver, (decimal?)null, (decimal?)null));

        await _harness.Worker.Round(CancellationToken.None);
        _harness.Orders.TradingOpen = false;
        At(Tuesday, hour: 22, minute: 0, second: 0);
        await _harness.Worker.Round(CancellationToken.None);
        _harness.Orders.Posted.Count.ShouldBe(1);

        _harness.Orders.TradingOpen = true;
        At(Tuesday.AddDays(1), hour: 4, minute: 0, second: 30);
        await _harness.Worker.Round(CancellationToken.None);

        PostOrderRequest remainder = _harness.Orders.Posted[^1];
        _harness.Orders.Posted.Count.ShouldBe(2);
        remainder.ShouldSatisfyAllConditions(
            () => remainder.OrderId.ShouldBe(TInvestOrderRequestIds.For(_harness.Bot, engineOrderId: 1, attempt: 2).ToString("D")),
            () => remainder.Quantity.ShouldBe(2),
            () => remainder.Price.ToDecimal().ShouldBe(100m));
        _harness.Waits.Reports[^1].ShouldBe((1L, (OrderWaitingCode?)null, (decimal?)null, (decimal?)null));

        _harness.Orders.Fill(remainder.OrderId, lots: 2, price: 99.75m, commission: 1m);
        IReadOnlyList<ExternalOrderUpdate> updates = await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.FinalFill);

        updates.ShouldNotContain(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed);
        updates.ShouldContain(ExternalOrderUpdate.Partial(orderId: 1, grossBase: 10m, grossQuote: 995m, fee: 0.5m));
        updates[^1].ShouldBe(ExternalOrderUpdate.Final(orderId: 1, grossBase: 30m, grossQuote: 2990m, fee: 1.5m));
    }

    [Test]
    public async Task AnOrderFilledDuringItsCarryOverEndsWithoutAnotherAttempt()
    {
        _harness.Orders.Schedule.Add(Weekday(Tuesday));
        await Reconcile();
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Sell, quantity: 10m, price: 101m));
        string first = _harness.Orders.Posted.Single().OrderId;
        _harness.Orders.Fill(first, lots: 1, price: 101m, commission: 0.5m);

        At(Tuesday, hour: 20, minute: 45, second: 0);
        await _harness.Worker.Round(CancellationToken.None);

        _harness.Attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Final);
        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.FinalFill))[^1]
            .ShouldBe(ExternalOrderUpdate.Final(orderId: 1, grossBase: 10m, grossQuote: 1010m, fee: 0.5m));
    }

    [Test]
    public async Task AnOwnerCancelOfACarriedRemainderEndsTheOrderLocally()
    {
        _harness.Orders.Schedule.Add(Weekday(Tuesday));
        await Reconcile();
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 20m, price: 100m));
        At(Tuesday, hour: 20, minute: 45, second: 0);
        await _harness.Worker.Round(CancellationToken.None);

        await _harness.Gateway.Cancel(_harness.Bot, _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId: 1), CancellationToken.None);

        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.CancelConfirmed))[^1].ShouldBe(ExternalOrderUpdate.ConfirmCancel(orderId: 1));
        _harness.Orders.Cancelled.Count.ShouldBe(1);
        _harness.Attempts.All.ShouldAllBe(attempt => attempt.State == TInvestOrderAttemptState.Final);
    }

    [Test]
    public async Task ACancellationNobodyAskedForIsUnresolved()
    {
        await Reconcile();
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 100m));

        _harness.Orders.Expire(_harness.Orders.Posted.Single().OrderId);

        var failure = await Should.ThrowAsync<OrderGatewaySafetyException>(() => _harness.Until(_ => false));
        failure.Reason.ShouldBe(BotStatusReason.UnresolvedOrder);
        BotReconciliationInternal reconciliation = await Reconcile(
            new ReconcileOrderInternal(EngineOrderId: 1, _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId: 1), MayHaveReachedVenue: true));
        reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
    }

    [Test]
    public async Task AHolidayIntervalCountsAsClosed_EvenWhileTheStatusIsOpen()
    {
        _harness.Orders.Schedule.Add(
            (
                Tuesday,
                [
                    ("holiday_trading_session_morning", Utc(Tuesday, hour: 7, minute: 0), Utc(Tuesday, hour: 16, minute: 0)),
                    ("dealer_holiday_trading_session_evening", Utc(Tuesday, hour: 16, minute: 0), Utc(Tuesday, hour: 20, minute: 49))
                ]
            ));

        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 100m));

        _harness.Orders.Posted.ShouldBeEmpty();
        _harness.Waits.Reports.ShouldBe([(1L, OrderWaitingCode.SessionClosed, null, null)]);
    }

    [Test]
    public async Task ADayWithAnUnknownTokenPlansNoCarryOver()
    {
        (DateOnly Date, List<(string Type, DateTimeOffset From, DateTimeOffset To)> Intervals) day = Weekday(Tuesday);
        day.Intervals.Add(("regular_trading_session_night", Utc(Tuesday, hour: 21, minute: 0), Utc(Tuesday, hour: 22, minute: 0)));
        _harness.Orders.Schedule.Add(day);
        await Reconcile();
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 100m));

        At(Tuesday, hour: 20, minute: 45, second: 0);
        await _harness.Worker.Round(CancellationToken.None);

        _harness.Orders.Cancelled.ShouldBeEmpty();
        _harness.Attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Active);
    }

    [Test]
    public async Task NothingIsCarriedOverBeforeTheLead()
    {
        _harness.Orders.Schedule.Add(Weekday(Tuesday));
        await Reconcile();
        await Submit(TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 100m));

        At(Tuesday, hour: 20, minute: 43, second: 59);
        await _harness.Worker.Round(CancellationToken.None);

        _harness.Orders.Cancelled.ShouldBeEmpty();
    }

    /// <summary>
    ///     The observed weekday of the calendar: every exchange interval with its specific token, the generic ones paired.
    /// </summary>
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

    private void At(DateOnly date, int hour, int minute, int second)
    {
        _harness.Clock.SetUtcNow(new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute, second)), TimeSpan.Zero));
    }

    private Task<ExternalOrderUpdate?> Submit(OrderSnapshot order)
    {
        return _harness.Gateway.Submit(
            new OrderSubmitInternal(_harness.Bot, _harness.Gateway.ClientOrderIdFor(_harness.Bot, order.Id), order, MustLookUpBeforeSubmit: false),
            TInvestGatewayHarness.NoOp,
            CancellationToken.None);
    }

    private Task<BotReconciliationInternal> Reconcile(params ReconcileOrderInternal[] orders)
    {
        return _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot, AllowForeignOrders: false, orders), CancellationToken.None);
    }
}