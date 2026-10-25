using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     Late T-Invest commissions (ADR 0023, T13): a fill applies at once with its commission pending; the worker reads the
///     final order again every few minutes and, once T-Invest reports a positive commission, records it and corrects the
///     order's fee once; nothing is looked for after the window. Values are invented.
/// </summary>
public sealed class TInvestLateCommissionTests
{
    private TInvestGatewayHarness _harness = null!;

    [SetUp]
    public async Task CreateGateway()
    {
        _harness = new TInvestGatewayHarness();
        await _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot, AllowForeignOrders: false, []), CancellationToken.None);
    }

    [Test]
    public async Task ALateCommissionCorrectsTheOrderOnce()
    {
        string requestId = await FilledWithoutCommission();

        await _harness.Worker.Round(CancellationToken.None);
        _harness.Orders.Commission(requestId, commission: 0.5m);
        await _harness.Worker.Round(CancellationToken.None);
        _harness.Attempts.All.ShouldHaveSingleItem().ExecutedFee.ShouldBe(0m);

        _harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await _harness.Worker.Round(CancellationToken.None);

        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.FeeCorrection))[^1]
            .ShouldBe(
                ExternalOrderUpdate.CorrectFee(
                    orderId: 1,
                    grossBase: 10m,
                    grossQuote: 1000m,
                    cumulativeFee: 0.5m,
                    commissionPending: false,
                    commissionPartitioned: true));
        _harness.Attempts.All.ShouldHaveSingleItem().ExecutedFee.ShouldBe(0.5m);

        _harness.Clock.Advance(TimeSpan.FromMinutes(5));
        int reads = _harness.Api.Calls.Count;
        await _harness.Worker.Round(CancellationToken.None);
        _harness.Api.Calls.Count.ShouldBe(reads);
    }

    [Test]
    public async Task ACommissionAfterTheWindowIsNotLookedFor()
    {
        string requestId = await FilledWithoutCommission();
        _harness.Clock.Advance(TimeSpan.FromDays(8));
        _harness.Orders.Commission(requestId, commission: 0.5m);

        await _harness.Worker.Round(CancellationToken.None);

        _harness.Attempts.All.ShouldHaveSingleItem().ExecutedFee.ShouldBe(0m);
    }

    /// <summary>
    ///     A limit buy of one lot filled at 100 without a commission yet, read from the order stream.
    /// </summary>
    private async Task<string> FilledWithoutCommission()
    {
        OrderSnapshot order = TInvestGatewayHarness.Limit(id: 1, OrderSide.Buy, quantity: 10m, price: 100m);
        await _harness.Gateway.Submit(
            new OrderSubmitInternal(_harness.Bot, _harness.Gateway.ClientOrderIdFor(_harness.Bot, order.Id), order, MustLookUpBeforeSubmit: false),
            TInvestGatewayHarness.NoOp,
            CancellationToken.None);
        string requestId = _harness.Orders.Posted.Single().OrderId;
        _harness.Orders.Fill(requestId, lots: 1, price: 100m, commission: 0m);

        (await _harness.Until(update => update.Kind == ExternalOrderUpdateKind.FinalFill))[^1]
            .ShouldBe(ExternalOrderUpdate.Final(orderId: 1, grossBase: 10m, grossQuote: 1000m, fee: 0m, commissionPending: true, commissionPartitioned: true));
        _harness.Attempts.All.ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                attempt => attempt.State.ShouldBe(TInvestOrderAttemptState.Final),
                attempt => attempt.ExecutedLots.ShouldBe(1));

        return requestId;
    }
}