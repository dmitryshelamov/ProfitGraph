using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Bybit.Simulation;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The in-memory Bybit execution end-to-end tests trade against: market orders fill at once, limit orders rest.
/// </summary>
public sealed class BybitSimulatedOrderGatewayTests
{
    private readonly BotId _bot = BotId.New();
    private BybitSimulatedOrderGateway _gateway = null!;
    private int _placeStarts;

    [SetUp]
    public void Create()
    {
        _gateway = new BybitSimulatedOrderGateway(new BybitSimulationOptions { Enabled = true, MarketPrice = 100m, FeeRate = 0.001m });
        _placeStarts = 0;
    }

    [Test]
    public async Task MarketBuyFillsAtTheConfiguredPriceWithABaseFee()
    {
        ExternalOrderUpdate? rejection = await Submit(BybitGatewayHarness.MarketBuy(id: 1, quoteBudget: 25m));

        rejection.ShouldBeNull();
        _placeStarts.ShouldBe(1);
        List<ExternalOrderUpdate> updates = await Published(count: 2);
        updates[0].ShouldBe(ExternalOrderUpdate.Accept(orderId: 1));
        updates[1].ShouldBe(ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.25m, grossQuote: 25m, fee: 0.00025m));
    }

    [Test]
    public async Task MarketSellPaysItsFeeInQuote()
    {
        OrderSnapshot sell = BybitGatewayHarness.LimitSell(id: 2, quantity: 0.5m, price: 120m) with { Type = OrderType.Market, LimitPrice = null };

        await Submit(sell);

        List<ExternalOrderUpdate> updates = await Published(count: 2);
        updates[1].ShouldBe(ExternalOrderUpdate.Final(orderId: 2, grossBase: 0.5m, grossQuote: 50m, fee: 0.05m));
    }

    [Test]
    public async Task LimitOrderRestsUntilCancelledOnce()
    {
        OrderSnapshot sell = BybitGatewayHarness.LimitSell(id: 3, quantity: 0.5m, price: 120m);
        ClientOrderId id = _gateway.ClientOrderIdFor(_bot, engineOrderId: 3);

        await Submit(sell);
        await _gateway.Cancel(_bot, id, CancellationToken.None);
        await _gateway.Cancel(_bot, id, CancellationToken.None);

        List<ExternalOrderUpdate> updates = await Published(count: 2);
        updates.ShouldBe([ExternalOrderUpdate.Accept(orderId: 3), ExternalOrderUpdate.ConfirmCancel(orderId: 3)]);
        (await Published(count: 0)).ShouldBeEmpty();
    }

    [Test]
    public async Task LookupRepublishesAPlacedOrderWithoutPlacingAgain()
    {
        OrderSnapshot buy = BybitGatewayHarness.MarketBuy(id: 4, quoteBudget: 10m);
        await Submit(buy);
        await Published(count: 2);

        await Submit(buy, mustLookUp: true);

        _placeStarts.ShouldBe(1);
        (await Published(count: 1)).Single().Kind.ShouldBe(ExternalOrderUpdateKind.FinalFill);
    }

    [Test]
    public async Task ReconciliationReportsOnlyOrdersThisProcessPlaced()
    {
        await Submit(BybitGatewayHarness.LimitSell(id: 5, quantity: 0.5m, price: 120m));
        ClientOrderId placed = _gateway.ClientOrderIdFor(_bot, engineOrderId: 5);
        ClientOrderId unknown = _gateway.ClientOrderIdFor(_bot, engineOrderId: 6);

        BotReconciliationInternal result = await _gateway.Reconcile(
            new BotReconcileInternal(
                _bot,
                AllowForeignOrders: false,
                [
                    new ReconcileOrderInternal(EngineOrderId: 5, placed, MayHaveReachedVenue: true),
                    new ReconcileOrderInternal(EngineOrderId: 6, unknown, MayHaveReachedVenue: true)
                ]),
            CancellationToken.None);

        result.Updates.ShouldBe([ExternalOrderUpdate.Accept(orderId: 5)]);
        result.Found.ShouldBe([new ReconciledOrderInternal(placed, IsFinal: false)]);
        result.Degradation.ShouldBeNull();
    }

    [Test]
    public void OptionsNeedAPositivePriceAndASmallFee()
    {
        new BybitSimulationOptions { MarketPrice = 100m }.IsValid().ShouldBeTrue();
        new BybitSimulationOptions { MarketPrice = 0m }.IsValid().ShouldBeFalse();
        new BybitSimulationOptions { MarketPrice = 100m, FeeRate = 0.1m }.IsValid().ShouldBeFalse();
        new BybitSimulationOptions { MarketPrice = 100m, Enabled = true }.Enabled.ShouldBeTrue();
    }

    private Task<ExternalOrderUpdate?> Submit(OrderSnapshot order, bool mustLookUp = false)
    {
        var submit = new OrderSubmitInternal(_bot, BybitClientOrderIds.For(_bot, order.Id), order, mustLookUp);

        return _gateway.Submit(
            submit,
            _ =>
            {
                _placeStarts++;

                return Task.CompletedTask;
            },
            CancellationToken.None);
    }

    private async Task<List<ExternalOrderUpdate>> Published(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(count == 0 ? 100 : 5000));
        var published = new List<ExternalOrderUpdate>();

        try
        {
            await foreach (ExternalOrderUpdate update in _gateway.Updates(_bot, timeout.Token))
            {
                published.Add(update);

                if (published.Count == count)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (count == 0)
        {
            // Nothing more was published.
        }

        return published;
    }
}