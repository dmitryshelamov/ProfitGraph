using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotFeed;
using ProfitGraph.Application.Bots.GetBotWaitingOrders;
using ProfitGraph.Application.Bots.RecordOrderWaiting;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Working orders a venue holds locally (ADR 0023, TC4): the module sets, changes and clears the reason, only the owner reads it,
///     and an order the engine no longer has working ends its wait at the next root.
/// </summary>
[NonParallelizable]
public sealed class OrderWaitingTests
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
    public async Task TheModuleSetsAndClearsAWait_AndOnlyTheOwnerReadsIt()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await Send(worker, new RecordOrderWaitingCommand(bot.Id.Value, EngineOrderId: 1, OrderWaitingCode.PriceBand, Low: 90m, High: 110m));
        await Send(worker, new RecordOrderWaitingCommand(bot.Id.Value, EngineOrderId: 1, OrderWaitingCode.PriceBand, Low: 91m, High: 111m));

        BotOrderWaitingInternal waiting = (await Waiting(worker, bot)).ShouldHaveSingleItem();
        waiting.ShouldSatisfyAllConditions(
            () => waiting.Code.ShouldBe(OrderWaitingCode.PriceBand),
            () => waiting.Low.ShouldBe(91m),
            () => waiting.High.ShouldBe(111m));
        await Should.ThrowAsync<ResourceNotFoundException>(() => Query(worker, new GetBotWaitingOrdersQuery(Guid.NewGuid(), bot.Id.Value)));

        await Send(worker, new RecordOrderWaitingCommand(bot.Id.Value, EngineOrderId: 1, Code: null));

        (await Waiting(worker, bot)).ShouldBeEmpty();
    }

    [Test]
    public async Task ANewReasonEndsTheCurrentWaitAndStartsAnother_AndARepeatIsQuiet()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        DateTimeOffset unlock = DateTimeOffset.UtcNow.AddDays(7);

        await Send(worker, new RecordOrderWaitingCommand(bot.Id.Value, EngineOrderId: 1, OrderWaitingCode.SettlementHold, unlock));
        await Send(worker, new RecordOrderWaitingCommand(bot.Id.Value, EngineOrderId: 1, OrderWaitingCode.AwaitingConfirmation));
        await Send(worker, new RecordOrderWaitingCommand(bot.Id.Value, EngineOrderId: 1, OrderWaitingCode.AwaitingConfirmation));

        BotOrderWaitingInternal waiting = (await Waiting(worker, bot)).ShouldHaveSingleItem();
        waiting.ShouldSatisfyAllConditions(
            () => waiting.Code.ShouldBe(OrderWaitingCode.AwaitingConfirmation),
            () => waiting.Until.ShouldBeNull());

        using IHost outbox = BotTestHost.Create(services => services.AddOutboxDispatcher());
        await outbox.StartAsync();
        await RuntimeScenario.Until(async () => (await WaitingFeed(worker, bot)).Length == 3, "the waits reach the feed");
        await outbox.StopAsync();
        BotFeedEntryInternal[] feed = await WaitingFeed(worker, bot);
        feed.Select(entry => (entry.Kind, entry.Details["code"]))
            .ShouldBe(
            [
                (BotFeedKind.OrderWaiting, nameof(OrderWaitingCode.SettlementHold)),
                (BotFeedKind.OrderWaitingEnded, nameof(OrderWaitingCode.SettlementHold)),
                (BotFeedKind.OrderWaiting, nameof(OrderWaitingCode.AwaitingConfirmation))
            ]);
    }

    [Test]
    public async Task AFilledOrderEndsItsWait()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the entry is accepted");
        await Send(worker, new RecordOrderWaitingCommand(bot.Id.Value, EngineOrderId: 1, OrderWaitingCode.SessionClosed));

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        await RuntimeScenario.Until(async () => (await Waiting(worker, bot)).Count == 0, "the filled order's wait ends");
        await worker.StopAsync();
    }

    private static async Task Send(IHost host, IRequest command)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private static async Task<IReadOnlyList<BotOrderWaitingInternal>> Query(IHost host, GetBotWaitingOrdersQuery query)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(query);
    }

    private static Task<IReadOnlyList<BotOrderWaitingInternal>> Waiting(IHost host, Bot bot)
    {
        return Query(host, new GetBotWaitingOrdersQuery(bot.OwnerId.Value, bot.Id.Value));
    }

    private static async Task<BotFeedEntryInternal[]> WaitingFeed(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();
        BotFeedPageInternal page = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetBotFeedQuery(bot.OwnerId.Value, bot.Id.Value, Before: null, Limit: 100));

        return
        [
            .. page.Items
                .Where(entry => entry.Kind is BotFeedKind.OrderWaiting or BotFeedKind.OrderWaitingEnded)
                .OrderBy(entry => entry.Sequence)
        ];
    }
}