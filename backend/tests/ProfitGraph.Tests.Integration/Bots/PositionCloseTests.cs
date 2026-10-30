using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotPositionClose;
using ProfitGraph.Application.Bots.RequestPositionClose;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;
using ProfitGraph.Domain.Bots.PositionCloses;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The owner's position close on the scripted venue (ADR 0064): the runner cancels the ladder's working orders, sells what the
///     position holds at the market once they are gone, ends the close and stops the bot; a bot without a position has nothing to
///     close.
/// </summary>
[NonParallelizable]
public sealed class PositionCloseTests
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
    public async Task ACloseCancelsTheOrdersSellsThePositionAndStopsTheBot()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker(withOutbox: true);
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await Orders(worker, bot) is [{ Status: OrderStatus.Active }], "the entry is accepted");
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Any(order => order is { Tag: "ladder.exit", Status: OrderStatus.Active }),
            "the exit is accepted");

        BotPositionCloseInternal requested = await Send(worker, new RequestPositionCloseCommand(bot.OwnerId.Value, bot.Id.Value, StopAfter: true));
        await RuntimeScenario.Until(async () => (await Latest(worker, bot)).Status == BotPositionCloseStatus.Cancelling, "the runner starts the close");
        IReadOnlyList<OrderSnapshot> cancelling = await Orders(worker, bot);
        cancelling.ShouldAllBe(order => order.Status == OrderStatus.PendingCancel);

        foreach (OrderSnapshot order in cancelling)
        {
            scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.ConfirmCancel(order.Id));
        }

        await RuntimeScenario.Until(
            async () => await Orders(worker, bot) is [{ Tag: PositionCloseOrder.Tag, Status: OrderStatus.Active }],
            "the position's sale is accepted");
        OrderSnapshot sale = (await Orders(worker, bot)).Single();
        sale.Side.ShouldBe(OrderSide.Sell);
        sale.Type.ShouldBe(OrderType.Market);
        sale.Quantity.ShouldBe(0.0999m);
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(sale.Id, grossBase: 0.0999m, grossQuote: 10.989m, fee: 0.010989m));
        await RuntimeScenario.Until(async () => (await Latest(worker, bot)).Status == BotPositionCloseStatus.Closed, "the close ends");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Feed(worker, bot.Id)).Any(entry => entry.Kind == BotFeedKind.PositionCloseEnded),
            "the feed reports the close");
        await worker.StopAsync();

        BotPositionCloseInternal closed = await Latest(worker, bot);
        closed.Id.ShouldBe(requested.Id);
        closed.CancelledOrders.ShouldBe(cancelling.Count);
        closed.EngineOrderId.ShouldBe(sale.Id);
        (await RuntimeScenario.Read(worker, bot.Id)).DesiredState.ShouldBe(BotDesiredState.Stopped);
        (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.ShouldSatisfyAllConditions(
            engine => engine.PositionClose.ShouldBeNull(),
            engine => engine.PositionOpenedAt.ShouldBeNull(),
            engine => engine.Account.BaseTotal.ShouldBe(0m));
        IReadOnlyList<BotFeedEntry> feed = await RuntimeScenario.Feed(worker, bot.Id);
        feed.Single(entry => entry.Kind == BotFeedKind.PositionCloseStarted).Details["stopAfter"].ShouldBe("true");
        feed.Single(entry => entry.Kind == BotFeedKind.PositionCloseEnded).Details["outcome"].ShouldBe("Closed");
    }

    [Test]
    public async Task ABotWithoutAPositionHasNothingToClose()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await Orders(worker, bot) is [{ Status: OrderStatus.Active }], "the entry is accepted");

        var refused = await Should.ThrowAsync<StateConflictException>(() => Send(
            worker,
            new RequestPositionCloseCommand(bot.OwnerId.Value, bot.Id.Value, StopAfter: false)));
        await worker.StopAsync();

        refused.Code.ShouldBe("position_close_no_position");
    }

    private static async Task<IReadOnlyList<OrderSnapshot>> Orders(IHost host, Bot bot)
    {
        return (await RuntimeScenario.Checkpoint(host, bot.Id))?.Engine.Account.Orders ?? [];
    }

    private static Task<BotPositionCloseInternal> Latest(IHost host, Bot bot)
    {
        return Send(host, new GetBotPositionCloseQuery(bot.OwnerId.Value, bot.Id.Value));
    }

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}