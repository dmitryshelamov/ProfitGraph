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
///     The owner's trade through the bot changes the bot (ADR 0037, as in Profit Fox): when a manual order of a Ladder bot fills, whole or
///     in part, the averaging order and the exit are cancelled and placed again, the exit on the whole position, as after the
///     strategy's own averaging fill.
/// </summary>
[NonParallelizable]
public sealed class ManualFillRebuildsLadderTests
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
    public async Task AFilledManualMarketBuyRebuildsTheGridOnTheWholePosition()
    {
        await RebuildAfter(limitPrice: null, partial: null, final: manualId => ExternalOrderUpdate.Final(manualId, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
    }

    [Test]
    public async Task AManualLimitBuyFilledInPartsRebuildsTheGridWhenItIsFilled_NotAtEachPart()
    {
        await RebuildAfter(
            limitPrice: 0.5m,
            partial: manualId => ExternalOrderUpdate.Partial(manualId, grossBase: 0.01m, grossQuote: 0.005m, fee: 0m),
            final: manualId => ExternalOrderUpdate.Final(manualId, grossBase: 0.02m, grossQuote: 0.01m, fee: 0m));
    }

    private async Task RebuildAfter(decimal? limitPrice, Func<long, ExternalOrderUpdate>? partial, Func<long, ExternalOrderUpdate> final)
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

        await Send(worker, new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Buy, Amount: 10m, LimitPrice: limitPrice));
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Places == 4), "the manual order is placed");
        long manualId = (await Orders(worker, bot)).Single(order => order.Tag == ManualOrder.Tag).Id;

        if (partial is not null)
        {
            scenario.Gateway.Push(bot.Id, partial(manualId));
            await Task.Delay(TimeSpan.FromSeconds(1));
            scenario.Gateway.Cancels.ShouldBeEmpty();
        }

        scenario.Gateway.Push(bot.Id, final(manualId));

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
