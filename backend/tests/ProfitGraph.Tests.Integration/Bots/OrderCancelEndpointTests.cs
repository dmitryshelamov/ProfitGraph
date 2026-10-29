using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.OrderCancels;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The owner's cancel of one of the bot's open orders over HTTP (ADR 0048): only the owner sees the bot, only a running bot or a
///     stopped one without a runner (ADR 0055) takes a cancel, only an order its engine holds as working and not already cancelling,
///     and only one at a time; the request is read back by its identifier. It has its own API, so its sign-ins do not share the other
///     bot tests' rate limit.
/// </summary>
[NonParallelizable]
public sealed class OrderCancelEndpointTests
{
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(BotApi.Settings());
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task ARunningBotTakesACancelOfAWorkingOrder_AndItIsReadBack()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        await SeedOrders(id);
        await Change(id, bot => bot.MarkRunning(DateTimeOffset.UtcNow));

        HttpResponseMessage accepted = await Cancel(owner.Client, id, engineOrderId: 2);
        var submitted = (await accepted.Content.ReadFromJsonAsync<OrderCancelResponse>(WebJson))!;
        HttpResponseMessage second = await Cancel(owner.Client, id, engineOrderId: 1);
        var read = (await owner.Client.GetFromJsonAsync<OrderCancelResponse>($"{BotApi.Path}/{id}/order-cancels/{submitted.Id}", WebJson))!;
        HttpResponseMessage unknown = await owner.Client.GetAsync($"{BotApi.Path}/{id}/order-cancels/{Guid.NewGuid()}");

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        accepted.Headers.Location!.ToString().ShouldEndWith($"/api/bots/{id}/order-cancels/{submitted.Id}");
        read.ShouldSatisfyAllConditions(
            () => read.Id.ShouldBe(submitted.Id),
            () => read.BotId.ShouldBe(id),
            () => read.EngineOrderId.ShouldBe(2),
            () => read.Status.ShouldBe(OrderCancelStatus.Pending),
            () => read.RejectionReason.ShouldBeNull(),
            () => read.ResolvedAt.ShouldBeNull());
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(second)).ShouldBe("order_cancel_request_pending");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BotApi.Code(unknown)).ShouldBe("order_cancel_not_found");
    }

    [Test]
    public async Task AnotherUsersBotIsNotFound()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        BotOwner stranger = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        await SeedOrders(id);
        await Change(id, bot => bot.MarkRunning(DateTimeOffset.UtcNow));
        HttpResponseMessage accepted = await Cancel(owner.Client, id, engineOrderId: 2);
        var submitted = (await accepted.Content.ReadFromJsonAsync<OrderCancelResponse>(WebJson))!;

        HttpResponseMessage cancel = await Cancel(stranger.Client, id, engineOrderId: 1);
        HttpResponseMessage read = await stranger.Client.GetAsync($"{BotApi.Path}/{id}/order-cancels/{submitted.Id}");

        cancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BotApi.Code(cancel)).ShouldBe("bot_not_found");
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AStartingOrStoppingBotRefuses()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await SeedOrders(id);

        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        HttpResponseMessage starting = await Cancel(owner.Client, id, engineOrderId: 2);
        await Change(id, bot => bot.MarkRunning(DateTimeOffset.UtcNow));
        await HoldLease(id);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/stop", content: null);
        HttpResponseMessage stopping = await Cancel(owner.Client, id, engineOrderId: 2);

        foreach (HttpResponseMessage response in new[] { starting, stopping })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await BotApi.Code(response)).ShouldBe(BotRuleException.NotRunning);
        }
    }

    [Test]
    public async Task AStoppedBotCancelsASentOrderAtOnce_AndRefusesOneThatNeverLeft()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await SeedOrders(id);
        await SeedSubmits(id);

        HttpResponseMessage accepted = await Cancel(owner.Client, id, engineOrderId: 2);
        var submitted = (await accepted.Content.ReadFromJsonAsync<OrderCancelResponse>(WebJson))!;
        HttpResponseMessage again = await Cancel(owner.Client, id, engineOrderId: 2);
        HttpResponseMessage unsent = await Cancel(owner.Client, id, engineOrderId: 1);
        BotOpenOrderResponse[] open = (await owner.Client.GetFromJsonAsync<BotOpenOrderResponse[]>($"{BotApi.Path}/{id}/open-orders", WebJson))!;

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        submitted.ShouldSatisfyAllConditions(
            () => submitted.EngineOrderId.ShouldBe(2),
            () => submitted.Status.ShouldBe(OrderCancelStatus.Requested),
            () => submitted.ResolvedAt.ShouldNotBeNull());
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(again)).ShouldBe("order_cancel_pending");
        unsent.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(unsent)).ShouldBe("order_not_sent");
        open.Single(order => order.EngineOrderId == 2).Status.ShouldBe(OrderStatus.PendingCancel);
    }

    [Test]
    public async Task AFinalOrAlreadyCancellingOrderRefuses()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        await SeedOrders(id);
        await Change(id, bot => bot.MarkRunning(DateTimeOffset.UtcNow));

        HttpResponseMessage filled = await Cancel(owner.Client, id, engineOrderId: 3);
        HttpResponseMessage neverPlaced = await Cancel(owner.Client, id, engineOrderId: 99);
        HttpResponseMessage cancelling = await Cancel(owner.Client, id, engineOrderId: 4);

        filled.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(filled)).ShouldBe("order_not_open");
        neverPlaced.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(neverPlaced)).ShouldBe("order_not_open");
        cancelling.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(cancelling)).ShouldBe("order_cancel_pending");
    }

    private static Task<HttpResponseMessage> Cancel(HttpClient client, Guid botId, long engineOrderId)
    {
        return client.PostAsync($"{BotApi.Path}/{botId}/open-orders/{engineOrderId}/cancel", content: null);
    }

    /// <summary>
    ///     The bot's engine holds a market buy being submitted (1), a resting exit (2), a filled order (3) and an order being cancelled (4).
    /// </summary>
    private async Task SeedOrders(Guid botId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var bots = scope.ServiceProvider.GetRequiredService<IBotRepository>();
        var checkpoints = scope.ServiceProvider.GetRequiredService<IBotCheckpointRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    Bot bot = (await bots.Get(new BotId(botId), CancellationToken.None))!;
                    await checkpoints.Save(
                        new BotCheckpoint(
                            bot.Id,
                            BotSeed.Checkpoint(
                                nextOrderId: 5,
                                BotSeed.MarketBuy(id: 1),
                                Limit(id: 2, OrderStatus.Active),
                                Limit(id: 3, OrderStatus.Filled),
                                Limit(id: 4, OrderStatus.PendingCancel)),
                            bot.Parameters,
                            DateTimeOffset.UtcNow),
                        CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    /// <summary>
    ///     The market buy (1) waits in the process; the exit (2) was sent to the venue.
    /// </summary>
    private async Task SeedSubmits(Guid botId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var intents = scope.ServiceProvider.GetRequiredService<IOrderIntentRepository>();
        var botIdentity = new BotId(botId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OrderIntent unsent = OrderIntent.Submit(OrderIntentId.New(), botIdentity, BotSeed.MarketBuy(id: 1), new ClientOrderId($"pg-{botId:N}-1"), now);
        OrderIntent sent = OrderIntent.Submit(OrderIntentId.New(), botIdentity, Limit(id: 2, OrderStatus.PendingSubmit), new ClientOrderId($"pg-{botId:N}-2"), now);
        sent.Claim(now);
        sent.MarkPlaceStarted(now);
        sent.MarkSent(now);
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () => await intents.TryAdd(unsent, CancellationToken.None) && await intents.TryAdd(sent, CancellationToken.None),
                CancellationToken.None);
    }

    /// <summary>
    ///     A runner holds the bot's lease, as one does until it stops.
    /// </summary>
    private async Task HoldLease(Guid botId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var leases = scope.ServiceProvider.GetRequiredService<IBotLeaseRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await leases.Add(BotLease.Acquire(new BotId(botId), LeaseHolderId.New(), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)), CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private async Task Change(Guid botId, Action<Bot> change)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var bots = scope.ServiceProvider.GetRequiredService<IBotRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    Bot bot = (await bots.GetForUpdate(new BotId(botId), CancellationToken.None))!;
                    change(bot);
                    await bots.Update(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private static OrderSnapshot Limit(long id, OrderStatus status)
    {
        return new OrderSnapshot(
            id,
            "ladder.exit",
            OrderSide.Sell,
            OrderType.Limit,
            Quantity: 0.1m,
            LimitPrice: 110m,
            QuoteBudget: null,
            Reservation: 0.1m,
            "exit",
            EligibleFromCandleIndex: 1,
            status);
    }
}