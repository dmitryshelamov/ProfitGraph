using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The Bybit gateway on USDT perpetuals (ADR 0061): orders name their hedge-mode side, exits are reduce-only, an entry sets the bot's
///     leverage first, a symbol in one-way mode stops the bot, funding is no foreign order, and an order's fee is its quote fee.
/// </summary>
public sealed class BybitLinearGatewayTests
{
    [Test]
    public async Task AShortEntrySellsOnTheShortSideAfterSettingTheLeverage()
    {
        var harness = new BybitGatewayHarness(linear: true, PositionDirection.Short, leverage: 3m);

        (await Submit(harness, Limit(id: 1, OrderSide.Sell, quantity: 0.5m, price: 100m))).ShouldBeNull();

        using JsonDocument leverage = JsonDocument.Parse(harness.Exchange.LeverageBodies.ShouldHaveSingleItem());
        (leverage.RootElement.GetProperty("category").GetString(), leverage.RootElement.GetProperty("buyLeverage").GetString(),
                leverage.RootElement.GetProperty("sellLeverage").GetString())
            .ShouldBe(("linear", "3", "3"));
        using JsonDocument order = JsonDocument.Parse(harness.Exchange.CreateBodies.ShouldHaveSingleItem());
        JsonElement body = order.RootElement;
        (body.GetProperty("category").GetString(), body.GetProperty("positionIdx").GetInt32(), body.GetProperty("reduceOnly").GetBoolean(),
                body.GetProperty("side").GetString(), body.GetProperty("qty").GetString(), body.GetProperty("price").GetString())
            .ShouldBe(("linear", 2, false, "Sell", "0.5", "100"));
        body.TryGetProperty("isLeverage", out _).ShouldBeFalse();
    }

    [Test]
    public async Task AShortCoverIsReduceOnlyAndSetsNoLeverage()
    {
        var harness = new BybitGatewayHarness(linear: true, PositionDirection.Short, leverage: 3m);

        await Submit(harness, Limit(id: 2, OrderSide.Buy, quantity: 0.5m, price: 90m));

        harness.Exchange.LeverageBodies.ShouldBeEmpty();
        using JsonDocument order = JsonDocument.Parse(harness.Exchange.CreateBodies.ShouldHaveSingleItem());
        (order.RootElement.GetProperty("positionIdx").GetInt32(), order.RootElement.GetProperty("reduceOnly").GetBoolean()).ShouldBe((2, true));
    }

    [Test]
    public async Task ALongMarketBuyOfAQuoteBudgetIsSizedInBaseAtTheLastPrice()
    {
        var harness = new BybitGatewayHarness(linear: true);

        await Submit(harness, BybitGatewayHarness.MarketBuy(id: 3, quoteBudget: 50.05m));

        using JsonDocument order = JsonDocument.Parse(harness.Exchange.CreateBodies.ShouldHaveSingleItem());
        JsonElement body = order.RootElement;

        // The mock's last price is 100: 50.05 buys 0.5005, floored to the 0.001 step.
        (body.GetProperty("qty").GetString(), body.GetProperty("positionIdx").GetInt32(), body.GetProperty("reduceOnly").GetBoolean())
            .ShouldBe(("0.5", 1, false));
        body.TryGetProperty("marketUnit", out _).ShouldBeFalse();
    }

    [Test]
    public async Task ALeverageBybitRefusesLeavesTheEntryUnsentAndOneAlreadySetIsSuccess()
    {
        var harness = new BybitGatewayHarness(linear: true, leverage: 2m);
        harness.Exchange.LeverageCode = 10001;

        await Should.ThrowAsync<BybitOrderUnresolvedException>(() => Submit(harness, Limit(id: 4, OrderSide.Buy, quantity: 0.5m, price: 100m)));
        harness.Exchange.CreateBodies.ShouldBeEmpty();

        harness.Exchange.LeverageCode = 110043;
        (await Submit(harness, Limit(id: 5, OrderSide.Buy, quantity: 0.5m, price: 100m))).ShouldBeNull();
        harness.Exchange.CreateBodies.ShouldHaveSingleItem();
    }

    [Test]
    public async Task ASymbolInOneWayModeStopsTheBot()
    {
        var harness = new BybitGatewayHarness(linear: true, PositionDirection.Short);
        harness.Exchange.Positions.Clear();
        harness.Exchange.Positions.Add((0, "0"));

        BotReconciliationInternal result = await harness.Gateway.Reconcile(
            new BotReconcileInternal(harness.Bot.Id, AllowForeignOrders: false, []),
            CancellationToken.None);

        result.Degradation.ShouldBe(BotStatusReason.VenuePositionMode);
        harness.PrivateStream.Connects.ShouldBeEmpty();
    }

    [Test]
    public async Task FundingIsNoForeignOrderAndTheStreamSubscribesToLinearTopics()
    {
        var harness = new BybitGatewayHarness(linear: true);
        harness.Exchange.FundingExecutions = 2;

        BotReconciliationInternal result = await harness.Gateway.Reconcile(
            new BotReconcileInternal(harness.Bot.Id, AllowForeignOrders: false, []),
            CancellationToken.None);

        result.Degradation.ShouldBeNull();
        harness.PrivateStream.Markets.ShouldHaveSingleItem().ShouldBe(BybitMarket.Linear);
    }

    [Test]
    public async Task AFilledLinearOrderReportsItsQuoteFee()
    {
        var harness = new BybitGatewayHarness(linear: true, PositionDirection.Short);
        string clientOrderId = BybitClientOrderIds.For(harness.Bot.Id, engineOrderId: 6).Value;
        harness.Exchange.AddOrder(clientOrderId, "Sell", "Filled", cumulativeBase: 0.5m, cumulativeQuote: 50m, new Dictionary<string, decimal> { ["USDT"] = 0.0275m });

        BotReconciliationInternal result = await harness.Gateway.Reconcile(
            new BotReconcileInternal(
                harness.Bot.Id,
                AllowForeignOrders: true,
                [new ReconcileOrderInternal(EngineOrderId: 6, BybitClientOrderIds.For(harness.Bot.Id, engineOrderId: 6), MayHaveReachedVenue: true)]),
            CancellationToken.None);

        result.Updates.ShouldBe([ExternalOrderUpdate.Accept(orderId: 6), ExternalOrderUpdate.Final(orderId: 6, grossBase: 0.5m, grossQuote: 50m, fee: 0.0275m)]);
    }

    [Test]
    public void TheLeverageSettingIsCheckedAndStored()
    {
        var schema = new BybitLinearBotSettingsSchema();
        using JsonDocument valid = JsonDocument.Parse("""{"leverage":"2.5"}""");
        using JsonDocument invalid = JsonDocument.Parse("""{"leverage":"0.5","other":1}""");

        BybitLinearBotSettingsSchema.Parse(schema.Read(valid.RootElement)).ShouldBe(2.5m);
        schema.Leverage(schema.Defaults).ShouldBe(1m);
        schema.Leverage(BotVenueSettings.None).ShouldBe(1m);
        Should.Throw<Exception>(() => schema.Read(invalid.RootElement));
    }

    private static OrderSnapshot Limit(long id, OrderSide side, decimal quantity, decimal price)
    {
        return new OrderSnapshot(
            id,
            "level",
            side,
            OrderType.Limit,
            quantity,
            price,
            QuoteBudget: null,
            quantity,
            "level",
            EligibleFromCandleIndex: 0,
            OrderStatus.PendingSubmit);
    }

    private static Task<ExternalOrderUpdate?> Submit(BybitGatewayHarness harness, OrderSnapshot order)
    {
        var submit = new OrderSubmitInternal(harness.Bot.Id, BybitClientOrderIds.For(harness.Bot.Id, order.Id), order, MustLookUpBeforeSubmit: false);

        return harness.Gateway.Submit(submit, _ => Task.CompletedTask, CancellationToken.None);
    }
}