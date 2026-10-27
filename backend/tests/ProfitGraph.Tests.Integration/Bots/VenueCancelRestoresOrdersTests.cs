using Microsoft.Extensions.Hosting;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     An order the venue cancelled without the bot asking (the owner's cancel at the broker) does not stop the bot (ADR 0039, as in
///     Profit Fox): the engine ends it as cancelled and one more resume lets the strategy place the exit again.
/// </summary>
[NonParallelizable]
public sealed class VenueCancelRestoresOrdersTests
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
    public async Task AnExitTheVenueCancelledIsPlacedAgainByTheStrategy_AndTheBotKeepsRunning()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningResumingLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)) is [{ Status: OrderStatus.Active }],
            "the entry is accepted");
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Count(order => order.Status == OrderStatus.Active) == 2,
            "the exit and the averaging are placed");
        OrderSnapshot exit = (await Orders(worker, bot)).Single(order => order.Tag == "ladder.exit");

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.ConfirmCancel(exit.Id));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Any(order => order.Tag == "ladder.exit" && order.Id > exit.Id && order.Status == OrderStatus.Active),
            "the strategy places the exit again");
        await worker.StopAsync();

        (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus.ShouldNotBe(BotRuntimeStatus.Degraded);
        (await Orders(worker, bot)).Count(order => order.Tag == "ladder.average" && order.Status == OrderStatus.Active).ShouldBe(1);
    }

    private static async Task<IReadOnlyList<OrderSnapshot>> Orders(IHost host, Bot bot)
    {
        return (await RuntimeScenario.Checkpoint(host, bot.Id))?.Engine.Account.Orders ?? [];
    }
}
