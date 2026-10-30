using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotFeed;
using ProfitGraph.Application.Bots.GetBotPosition;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Entry fees in the bot's reports (ADR 0063) on the scripted venue: a buy's base fee stays in the cost basis and the average
///     cost strategies see, while the position reports it as spent and the feed carries it with its asset.
/// </summary>
[NonParallelizable]
public sealed class BotPositionFeesTests
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
    public async Task ABuysBaseFeeIsReportedAsSpentButStaysInTheCost()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker(withOutbox: true);
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the entry is accepted");

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.PositionOpenedAt is not null,
            "the position opens");
        BotFeedEntryInternal? filled = null;
        await RuntimeScenario.Until(
            async () => (filled = (await Feed(worker, bot)).FirstOrDefault(entry => entry.Kind == BotFeedKind.Fill)) is not null,
            "the fill reaches the feed");
        BotPositionInternal position = await Position(worker, bot);
        await worker.StopAsync();

        position.BaseTotal.ShouldBe(0.0999m);
        position.CostBasis.ShouldBe(10m);
        position.EntryFees.ShouldBe(0.01m);
        position.AverageCost.ShouldBe(10m / 0.0999m);
        position.AverageBuyPrice.ShouldBe(100m);
        position.RealizedPnl.ShouldBe(-0.01m);
        position.PositionFees.ShouldBe(0.01m);
        position.Cycles.ShouldHaveSingleItem().Fees.ShouldBe(0.01m);
        filled!.Details["fee"].ShouldBe("0.0001");
        filled.Details["feeAsset"].ShouldBe("Base");
    }

    private static async Task<BotPositionInternal> Position(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotPositionQuery(bot.OwnerId.Value, bot.Id.Value, CycleLimit: 5));
    }

    private static async Task<IReadOnlyList<BotFeedEntryInternal>> Feed(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotFeedQuery(bot.OwnerId.Value, bot.Id.Value, Before: null, Limit: 50))).Items;
    }
}