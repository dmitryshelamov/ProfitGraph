using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.SubmitManualOrder;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The owner's purchase through the bot changes the bot (ADR 0037, as in Profit Fox): when a manual market buy of a Ladder bot is
///     filled, the averaging order and the exit are cancelled and placed again, the exit on the whole position, as after the strategy's
///     own averaging fill.
/// </summary>
[NonParallelizable]
public sealed class ManualBuyRebuildsLadderTests
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
    public async Task AFilledManualBuyCancelsTheAveragingAndTheExitAndPlacesThemAgainOnTheWholePosition()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningResumingLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the entry is accepted");
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Count(order => order.Status == OrderStatus.Active) == 2,
            "the exit and the averaging are placed");
        OrderSnapshot firstExit = (await Orders(worker, bot)).Single(order => order.Tag == "ladder.exit");
        OrderSnapshot firstAverage = (await Orders(worker, bot)).Single(order => order.Tag == "ladder.average");

        ManualOrderInternal manual = await Send(worker, new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Buy, Amount: 10m));
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Places == 4), "the manual buy is placed");
        long manualId = (await Orders(worker, bot)).Single(order => order.Tag == ManualOrder.Tag).Id;
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(manualId, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));

        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Cancels.Count >= 1), "the averaging is cancelled first");
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.ConfirmCancel(firstAverage.Id));
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Cancels.Count >= 2), "then the exit is cancelled");
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.ConfirmCancel(firstExit.Id));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Count(order => order.Status == OrderStatus.Active && order.Id > manualId) == 2,
            "the exit and the averaging are placed again");
        await worker.StopAsync();

        IReadOnlyList<OrderSnapshot> orders = await Orders(worker, bot);
        OrderSnapshot exit = orders.Single(order => order.Tag == "ladder.exit" && order.Id > manualId);
        OrderSnapshot average = orders.Single(order => order.Tag == "ladder.average" && order.Id > manualId);

        manual.Type.ShouldBe(OrderType.Market);
        exit.Quantity.ShouldNotBeNull().ShouldBeGreaterThan(firstExit.Quantity.ShouldNotBeNull());
        average.LimitPrice.ShouldNotBeNull();
        (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus.ShouldNotBe(BotRuntimeStatus.Degraded);
    }

    private static async Task<IReadOnlyList<OrderSnapshot>> Orders(IHost host, Bot bot)
    {
        return (await RuntimeScenario.Checkpoint(host, bot.Id))?.Engine.Account.Orders ?? [];
    }

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
