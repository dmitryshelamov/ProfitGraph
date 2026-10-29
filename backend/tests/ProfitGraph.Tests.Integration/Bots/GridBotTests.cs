using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.KillBot;
using ProfitGraph.Application.Bots.StartBot;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A grid bot on the scripted venue (ADR 0058, sections 9 and 10): its buys rest below the flat price, a bought level sells
///     what it bought, the levels survive a restart, a venue's cancel is placed again, the kill switch cancels without selling, and
///     a venue without room refuses the start.
/// </summary>
[NonParallelizable]
public sealed class GridBotTests
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
    public async Task ABoughtLevelSellsAndBuysAgain_AcrossARestart()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningGrid(worker, Grid());
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Count(order => order.Status == OrderStatus.Active) == 2,
            "the grid's two buys rest");
        OrderSnapshot buy = (await Orders(worker, bot)).Single(order => order.Tag == "grid.grid.1.buy");
        buy.LimitPrice.ShouldBe(95m);

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(buy.Id, grossBase: 0.1052m, grossQuote: 9.994m, fee: 0.0001m));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Any(order => order.Tag == "grid.grid.1.sell" && order.Status == OrderStatus.Active),
            "the level's sale rests");
        OrderSnapshot sale = (await Orders(worker, bot)).Single(order => order.Tag == "grid.grid.1.sell");
        sale.Quantity.ShouldBe(0.1051m);
        await worker.StopAsync();

        using IHost second = scenario.Worker();
        await second.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Lease(bot.Id))?.Epoch == 2, "the second Worker takes the lease");
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(second, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs again");
        (await RuntimeScenario.Checkpoint(second, bot.Id))!.Engine.Grids.ShouldHaveSingleItem().Levels.ShouldHaveSingleItem().HeldBase.ShouldBe(0.1051m);

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(sale.Id, grossBase: 0.1051m, grossQuote: 10.4996m, fee: 0.0105m));
        await RuntimeScenario.Until(
            async () => (await Orders(second, bot)).Any(order => order.Tag == "grid.grid.1.buy" && order.Id > sale.Id && order.Status == OrderStatus.Active),
            "the level buys again");
        await second.StopAsync();

        (await RuntimeScenario.Read(second, bot.Id)).RuntimeStatus.ShouldNotBe(BotRuntimeStatus.Degraded);
    }

    [Test]
    public async Task ABuyTheVenueCancelledIsPlacedAgain()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningGrid(worker, Grid());
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Count(order => order.Status == OrderStatus.Active) == 2,
            "the grid's two buys rest");
        OrderSnapshot buy = (await Orders(worker, bot)).Single(order => order.Tag == "grid.grid.2.buy");

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.ConfirmCancel(buy.Id));
        await RuntimeScenario.Until(
            async () => (await Orders(worker, bot)).Any(order => order.Tag == "grid.grid.2.buy" && order.Id > buy.Id && order.Status == OrderStatus.Active),
            "the grid places the buy again");
        await worker.StopAsync();

        (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus.ShouldNotBe(BotRuntimeStatus.Degraded);
    }

    [Test]
    public async Task TheKillSwitchCancelsEveryGridOrderWithoutSellingOrPlacingMore()
    {
        var scenario = new RuntimeScenario();
        Bot bot;

        using (IHost first = scenario.Worker())
        {
            bot = await RuntimeScenario.RunningGrid(first, Grid());
            await first.StartAsync();
            await RuntimeScenario.Until(
                async () => (await Orders(first, bot)).Count(order => order.Status == OrderStatus.Active) == 2,
                "the grid's two buys rest");
            await first.StopAsync();
        }

        using IHost second = scenario.Worker();

        using (IServiceScope scope = second.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new KillBotCommand(bot.OwnerId.Value, bot.Id.Value));
        }

        await second.StartAsync();
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Cancels.Count == 2), "the kill cancels both buys");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await second.StopAsync();

        scenario.Gateway.Places.ShouldBe(2);
        Bot killed = await RuntimeScenario.Read(second, bot.Id);
        killed.StatusReason.ShouldBe(BotStatusReason.Killed);
        killed.DesiredState.ShouldBe(BotDesiredState.Stopped);
        (await RuntimeScenario.Checkpoint(second, bot.Id))!.Engine.Grids.ShouldHaveSingleItem().Phase.ShouldBe(GridPhase.Running);
    }

    [Test]
    public async Task AVenueWithoutRoomForTheGridRefusesTheStart()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningGrid(worker, Grid());
        _api.Services.GetRequiredService<FakeRestingOrderCapacity>().Limit = 3;

        try
        {
            using IServiceScope scope = _api.Services.CreateScope();
            var refused = await Should.ThrowAsync<ApplicationValidationException>(() =>
                scope.ServiceProvider.GetRequiredService<ISender>().Send(new StartBotCommand(bot.OwnerId.Value, bot.Id.Value)));

            refused.Errors.ShouldContain(error => error.Code == BotErrorCodes.GridExceedsVenueOrders);
        }
        finally
        {
            _api.Services.GetRequiredService<FakeRestingOrderCapacity>().Limit = FakeRestingOrderCapacity.DefaultLimit;
        }
    }

    private static StrategyDefinition Grid()
    {
        return GridStrategyBlockFactory.Create(levels: 2, step: 0.05m, profitStep: 0.05m, lot: 10m, maxOpenBuys: 2);
    }

    private static async Task<IReadOnlyList<OrderSnapshot>> Orders(IHost host, Bot bot)
    {
        return (await RuntimeScenario.Checkpoint(host, bot.Id))?.Engine.Account.Orders ?? [];
    }
}