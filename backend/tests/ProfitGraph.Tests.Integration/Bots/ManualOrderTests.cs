using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetManualOrder;
using ProfitGraph.Application.Bots.SubmitManualOrder;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.ManualOrders;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Owner market and limit orders in live bots (stage 5.11, Profit Fox Designer ADR 0091): the runner resolves a request with the
///     root's checkpoint and one intent, a bot has one pending request, and one nobody takes expires.
/// </summary>
[NonParallelizable]
public sealed class ManualOrderTests
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
    public async Task RequestIsResolvedWithTheCheckpointAndGivesOneIntent()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");

        ManualOrderInternal submitted = await Send(worker, new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Buy, Amount: 10m));
        await RuntimeScenario.Until(
            async () => (await Send(worker, new GetManualOrderQuery(bot.OwnerId.Value, bot.Id.Value, submitted.Id))).Status == ManualOrderStatus.Submitted,
            "the runner takes the request");
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Places == 2), "the manual order is placed");
        await worker.StopAsync();

        ManualOrderInternal resolved = await Send(worker, new GetManualOrderQuery(bot.OwnerId.Value, bot.Id.Value, submitted.Id));
        long engineOrderId = resolved.EngineOrderId.ShouldNotBeNull();
        (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.Account.Orders.Single(order => order.Id == engineOrderId).Tag.ShouldBe(ManualOrder.Tag);
        (await RuntimeScenario.Intents(worker, bot.Id)).Count(intent => intent.EngineOrderId == engineOrderId).ShouldBe(1);
    }

    [Test]
    public async Task LimitRequestBecomesALimitEngineOrderAtItsPrice()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");

        ManualOrderInternal submitted = await Send(
            worker,
            new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Buy, Amount: 10m, LimitPrice: 0.5m));
        await RuntimeScenario.Until(
            async () => (await Send(worker, new GetManualOrderQuery(bot.OwnerId.Value, bot.Id.Value, submitted.Id))).Status == ManualOrderStatus.Submitted,
            "the runner takes the request");
        await worker.StopAsync();

        ManualOrderInternal resolved = await Send(worker, new GetManualOrderQuery(bot.OwnerId.Value, bot.Id.Value, submitted.Id));
        long engineOrderId = resolved.EngineOrderId.ShouldNotBeNull();
        OrderSnapshot order = (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.Account.Orders.Single(item => item.Id == engineOrderId);

        submitted.Type.ShouldBe(OrderType.Limit);
        resolved.LimitPrice.ShouldBe(0.5m);
        order.Type.ShouldBe(OrderType.Limit);
        order.Tag.ShouldBe(ManualOrder.Tag);
        order.LimitPrice.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(0.5m);
    }

    [Test]
    public async Task BotHasOnePendingRequestAndAStoppedBotTakesNone()
    {
        using IHost host = BotTestHost.Create();
        Bot bot = await RunningWithoutRunner(host);

        await Send(host, new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Buy, Amount: 10m));
        var pending = await Should.ThrowAsync<StateConflictException>(() => Send(
            host,
            new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Sell, Amount: 0.1m)));
        await BotStore.Change(host, bot.Id, stored => stored.Stop(DateTimeOffset.UtcNow));
        var stopped = await Should.ThrowAsync<BotRuleException>(() => Send(
            host,
            new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Buy, Amount: 10m)));

        pending.Code.ShouldBe("manual_order_pending");
        stopped.Code.ShouldBe(BotRuleException.NotRunning);
    }

    [Test]
    public async Task RequestNobodyTookExpiresAndMakesRoomForTheNext()
    {
        using IHost host = BotTestHost.Create();
        Bot bot = await RunningWithoutRunner(host);
        var stale = ManualOrderRequest.Create(ManualOrderRequestId.New(), bot.Id, OrderSide.Buy, amount: 10m, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2));
        await InTransaction(host, services => services.GetRequiredService<IManualOrderRequestRepository>().TryAdd(stale, CancellationToken.None));

        ManualOrderInternal seen = await Send(host, new GetManualOrderQuery(bot.OwnerId.Value, bot.Id.Value, stale.Id.Value));
        await Send(host, new SubmitManualOrderCommand(bot.OwnerId.Value, bot.Id.Value, OrderSide.Buy, Amount: 10m));

        seen.Status.ShouldBe(ManualOrderStatus.Rejected);
        seen.RejectionReason.ShouldBe("expired");
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<string>("SELECT rejection_reason FROM bot_manual_orders WHERE id = @id", new { id = stale.Id.Value })).ShouldBe("expired");
    }

    private static async Task<Bot> RunningWithoutRunner(IHost host)
    {
        Bot bot = await RuntimeScenario.RunningLadder(host);
        await BotStore.Change(host, bot.Id, stored => stored.MarkRunning(DateTimeOffset.UtcNow));

        return bot;
    }

    private static async Task<T> Send<T>(IHost host, IRequest<T> request)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private static async Task InTransaction(IHost host, Func<IServiceProvider, Task> action)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await action(scope.ServiceProvider);

                    return true;
                },
                CancellationToken.None);
    }
}