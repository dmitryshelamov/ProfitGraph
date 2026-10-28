using Grpc.Core;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Trading;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The bot's order state stream (ADR 0023, ADR 0044): a lost stream subscribes again, reads the bot's orders and the
///     account's active orders on the share once T-Invest confirmed it, and ends the bot's updates only when it stays lost for
///     the whole budget or another order trades on the share. Values are invented; the API is the in-process mock.
/// </summary>
public sealed class TInvestOrderStreamTests
{
    private TInvestGatewayHarness _harness = null!;

    [SetUp]
    public void Create()
    {
        _harness = new TInvestGatewayHarness();
    }

    [Test]
    public async Task LostStreamSubscribesAgainAndPublishesAFillItMissedOnce()
    {
        (await Reconcile()).Degradation.ShouldBeNull();
        await _harness.Gateway.Submit(Submit(engineOrderId: 1), TInvestGatewayHarness.NoOp, CancellationToken.None);
        string requestId = _harness.Orders.Posted.Single().OrderId;

        // The submit's answer and the stream's message of the new order are both read before the stream is lost.
        (await _harness.Next(count: 2)).ShouldBe([ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.Accept(orderId: 1)]);
        _harness.OrdersToRefresh.Add(
            new ReconcileOrderInternal(
                EngineOrderId: 1,
                _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId: 1),
                MayHaveReachedVenue: true,
                AcceptedByEngine: true));

        _harness.Orders.DropStreams();
        _harness.Orders.Fill(requestId, lots: 1, price: 99m, commission: 0.5m);
        await _harness.AdvanceUntil(() => _harness.Orders.Subscriptions == 2);

        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.FinalFill))[^1]
            .ShouldBe(ExternalOrderUpdate.Final(orderId: 1, grossBase: 10m, grossQuote: 990m, fee: 0.5m, commissionPartitioned: true));
        (await _harness.NextSoon()).ShouldBeEmpty();
        _harness.Attempts.All.ShouldHaveSingleItem().State.ShouldBe(TInvestOrderAttemptState.Final);
    }

    [Test]
    public async Task StreamThatStaysLostForTheWholeBudgetEndsTheUpdatesSoTheBotReconcilesAgain()
    {
        (await Reconcile()).Degradation.ShouldBeNull();

        _harness.Orders.StreamFailure = MockTInvestApi.Refusal(StatusCode.Unavailable, "connection reset");
        _harness.Orders.DropStreams();
        Task<IReadOnlyList<ExternalOrderUpdate>> reading = _harness.Next(count: 1);
        await _harness.AdvanceUntil(() => reading.IsCompleted);

        await Should.ThrowAsync<TInvestRpcException>(() => reading);
        _harness.Orders.Subscriptions.ShouldBe(1);
    }

    [Test]
    public async Task ForeignOrderPlacedWhileTheStreamWasLostEndsTheUpdatesWithASafetyReason()
    {
        (await Reconcile()).Degradation.ShouldBeNull();

        _harness.Orders.DropStreams();
        _harness.Orders.AddForeign(TInvestGatewayHarness.ShareUid);
        Task<IReadOnlyList<ExternalOrderUpdate>> reading = _harness.Next(count: 1);
        await _harness.AdvanceUntil(() => reading.IsCompleted);

        (await Should.ThrowAsync<OrderGatewaySafetyException>(() => reading)).Reason.ShouldBe(BotStatusReason.ForeignOrder);
        _harness.Orders.Subscriptions.ShouldBe(2);
    }

    private Task<BotReconciliationInternal> Reconcile()
    {
        return _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot, AllowForeignOrders: false, []), CancellationToken.None);
    }

    private OrderSubmitInternal Submit(long engineOrderId)
    {
        return new OrderSubmitInternal(
            _harness.Bot,
            _harness.Gateway.ClientOrderIdFor(_harness.Bot, engineOrderId),
            TInvestGatewayHarness.Limit(engineOrderId, OrderSide.Buy, quantity: 10m, price: 99m),
            MustLookUpBeforeSubmit: false);
    }
}