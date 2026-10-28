using System.Globalization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.CancelBotOrder;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetOrderCancel;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;
using ProfitGraph.Domain.Bots.OrderCancels;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The owner's cancel of one of a running bot's orders (ADR 0048) on the scripted venue: the runner turns the request into the
///     engine's ordinary cancel, so one cancel intent goes out, and the venue's confirmation ends the order and resumes the strategy, so
///     the Ladder places its exit again (ADR 0051). A deferred submit is cancelled locally, and the runner rejects a request for an
///     order that is not working or one that waited too long.
/// </summary>
[NonParallelizable]
public sealed class OwnerOrderCancelTests
{
    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [SetUp]
    public Task Isolate()
    {
        return RuntimeScenario.StopEveryBot();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task TheOwnersCancelOfTheLadderExitGoesOutOnce_AndTheStrategyPlacesTheExitAgain()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker(withOutbox: true);
        Bot bot = await RuntimeScenario.RunningResumingLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await Orders(worker, bot) is [{ Status: OrderStatus.Active }], "the entry is accepted");
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Count(order => order.Status == OrderStatus.Active) == 2,
            "the exit and the averaging are placed");
        OrderSnapshot exit = (await Orders(worker, bot)).Single(order => order.Tag == "ladder.exit");
        OrderSnapshot average = (await Orders(worker, bot)).Single(order => order.Tag == "ladder.average");
        ClientOrderId exitClientId = scenario.Gateway.ClientOrderIdFor(bot.Id, exit.Id);

        OrderCancelInternal requested = await Send(worker, new CancelBotOrderCommand(bot.OwnerId.Value, bot.Id.Value, exit.Id));
        await RuntimeScenario.Until(
            async () => (await Send(worker, new GetOrderCancelQuery(bot.OwnerId.Value, bot.Id.Value, requested.Id))).Status == OrderCancelStatus.Requested,
            "the runner hands the cancel to the engine");
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Cancels.Contains(exitClientId)), "the cancel goes out");
        (await Orders(worker, bot)).Single(order => order.Id == exit.Id).Status.ShouldBe(OrderStatus.PendingCancel);

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.ConfirmCancel(exit.Id));
        await RuntimeScenario.Until(async () => (await Orders(worker, bot)).All(order => order.Id != exit.Id), "the venue's confirmation ends the exit");

        // The root that ended the exit carries the exit placed again by the resume.
        IReadOnlyList<OrderSnapshot> after = await Orders(worker, bot);
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Feed(worker, bot.Id)).Any(entry => entry.Kind == BotFeedKind.OrderCancelRequested),
            "the feed records the owner's cancel");
        await worker.StopAsync();

        OrderSnapshot restored = after.Where(order => order.Tag == "ladder.exit").ShouldHaveSingleItem();
        restored.Id.ShouldNotBe(exit.Id);
        restored.Side.ShouldBe(exit.Side);
        restored.Quantity.ShouldBe(exit.Quantity);
        restored.LimitPrice.ShouldBe(exit.LimitPrice);
        after.Count.ShouldBe(2);
        after.ShouldContain(order => order.Id == average.Id);
        scenario.Gateway.Cancels.Count(id => id == exitClientId).ShouldBe(1);
        (await RuntimeScenario.Intents(worker, bot.Id)).Count(intent => intent.Kind == OrderIntentKind.Cancel).ShouldBe(1);
        (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus.ShouldNotBe(BotRuntimeStatus.Degraded);
        (await RuntimeScenario.Feed(worker, bot.Id)).Single(entry => entry.Kind == BotFeedKind.OrderCancelRequested)
            .Details["engineOrderId"]
            .ShouldBe(exit.Id.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task TheOwnersCancelOfADeferredSubmitIsLocal()
    {
        var scenario = new RuntimeScenario { Readiness = { Hold = OrderWaitingCode.SettlementHold } };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Any(intent => intent.Status == OrderIntentStatus.Deferred) &&
                await Orders(worker, bot) is [{ Status: OrderStatus.Active }],
            "the submit is deferred");

        await Send(worker, new CancelBotOrderCommand(bot.OwnerId.Value, bot.Id.Value, EngineOrderId: 1));
        await RuntimeScenario.Until(async () => (await Orders(worker, bot)).Count == 0, "the deferred submit is cancelled");
        await worker.StopAsync();

        IReadOnlyList<OrderIntent> intents = await RuntimeScenario.Intents(worker, bot.Id);
        intents.Single(intent => intent.Kind == OrderIntentKind.Submit).Rejection!.Reason.ShouldBe(RejectionReason.CancelledBeforeSend);
        intents.Single(intent => intent.Kind == OrderIntentKind.Cancel).Status.ShouldBe(OrderIntentStatus.Sent);
        scenario.Gateway.Cancels.ShouldBeEmpty();
        scenario.Gateway.Places.ShouldBe(0);
    }

    [Test]
    public async Task TheRunnerRejectsACancelThatWaitedTooLongAndOneOfAnOrderThatIsNotWorking()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await Orders(worker, bot) is [{ Status: OrderStatus.Active }], "the entry is accepted");

        var stale = OrderCancelRequest.Create(OrderCancelRequestId.New(), bot.Id, engineOrderId: 1, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2));
        await Add(worker, stale);
        await RuntimeScenario.Until(async () => (await Stored(worker, stale.Id)).Status == OrderCancelStatus.Rejected, "the stale cancel is resolved");
        var unknown = OrderCancelRequest.Create(OrderCancelRequestId.New(), bot.Id, engineOrderId: 99, DateTimeOffset.UtcNow);
        await Add(worker, unknown);
        await RuntimeScenario.Until(async () => (await Stored(worker, unknown.Id)).Status == OrderCancelStatus.Rejected, "the cancel of no order is resolved");
        await worker.StopAsync();

        (await Stored(worker, stale.Id)).RejectionReason.ShouldBe("expired");
        (await Stored(worker, unknown.Id)).RejectionReason.ShouldBe("order_not_open");
        (await Orders(worker, bot)).ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.Active);
        scenario.Gateway.Cancels.ShouldBeEmpty();
    }

    private static async Task<IReadOnlyList<OrderSnapshot>> Orders(IHost host, Bot bot)
    {
        return (await RuntimeScenario.Checkpoint(host, bot.Id))?.Engine.Account.Orders ?? [];
    }

    /// <summary>
    ///     The request as stored: a read through the query shows an untaken one as expired before the runner stores it so.
    /// </summary>
    private static async Task<OrderCancelRequest> Stored(IHost host, OrderCancelRequestId id)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IOrderCancelRequestRepository>().Get(id, CancellationToken.None))!;
    }

    private static async Task Add(IHost host, OrderCancelRequest request)
    {
        using IServiceScope scope = host.Services.CreateScope();
        bool added = await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                () => scope.ServiceProvider.GetRequiredService<IOrderCancelRequestRepository>().TryAdd(request, CancellationToken.None),
                CancellationToken.None);
        added.ShouldBeTrue();
    }

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}