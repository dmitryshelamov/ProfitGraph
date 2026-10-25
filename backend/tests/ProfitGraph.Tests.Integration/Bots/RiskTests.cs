using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Bots.KillBot;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Risk controls of live bots with the scripted venue (stage 5.9, Profit Fox Designer ADR 0056): a kill rejects the
///     unsent submit and cancels the sent order without selling, the per-order limit rejects before sending and an
///     insufficient balance degrades the bot.
/// </summary>
[NonParallelizable]
public sealed class RiskTests
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
    public async Task KillRejectsTheUnsentSubmitAndCancelsTheSentOrderWithoutSelling()
    {
        var scenario = new RuntimeScenario();
        Bot bot;

        using (IHost first = scenario.Worker())
        {
            bot = await RuntimeScenario.RunningLadder(first);
            await first.StartAsync();
            await RuntimeScenario.Until(
                async () => (await RuntimeScenario.Checkpoint(first, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
                "the entry is accepted");
            await first.StopAsync();
        }

        using IHost second = scenario.Worker();
        OrderIntent unsent = OrderIntent.Submit(
            OrderIntentId.New(),
            bot.Id,
            BotSeed.MarketBuy(id: 2),
            scenario.Gateway.ClientOrderIdFor(bot.Id, engineOrderId: 2),
            DateTimeOffset.UtcNow);
        await Add(second, unsent);

        using (IServiceScope scope = second.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new KillBotCommand(bot.OwnerId.Value, bot.Id.Value));
        }

        await second.StartAsync();
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Gateway.Cancels.Count == 1), "the kill cancel is sent");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await second.StopAsync();

        IReadOnlyList<OrderIntent> intents = await RuntimeScenario.Intents(second, bot.Id);
        Bot killed = await RuntimeScenario.Read(second, bot.Id);
        intents.Single(intent => intent.Id == unsent.Id).Rejection!.Reason.ShouldBe(RejectionReason.KillSwitch);
        intents.Single(intent => intent.Kind == OrderIntentKind.Cancel).FromKill.ShouldBeTrue();
        scenario.Gateway.Cancels.ShouldBe([scenario.Gateway.ClientOrderIdFor(bot.Id, engineOrderId: 1)]);
        scenario.Gateway.Places.ShouldBe(1);
        killed.StatusReason.ShouldBe(BotStatusReason.Killed);
        killed.DesiredState.ShouldBe(BotDesiredState.Stopped);
    }

    [Test]
    public async Task SubmitAboveThePerOrderLimitIsRejectedBeforeItIsSent()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE bots SET max_order_quote = 5 WHERE id = @id", new { id = bot.Id.Value });
        }

        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Intents(worker, bot.Id)).Any(intent => intent.Status == OrderIntentStatus.Rejected),
            "the submit is rejected");
        await worker.StopAsync();

        (await RuntimeScenario.Intents(worker, bot.Id)).First().Rejection!.Reason.ShouldBe(RejectionReason.RiskLimit);
        scenario.Gateway.Places.ShouldBe(0);
    }

    [Test]
    public async Task InsufficientBalanceRejectionDegradesTheBot()
    {
        var scenario = new RuntimeScenario { Gateway = { RejectWith = RejectionReason.InsufficientQuote } };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).StatusReason == BotStatusReason.InsufficientBalance, "the bot degrades");
        await worker.StopAsync();

        (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus.ShouldBe(BotRuntimeStatus.Degraded);
    }

    private static async Task Add(IHost host, OrderIntent intent)
    {
        using IServiceScope scope = host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderIntentRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await repository.TryAdd(intent, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }
}