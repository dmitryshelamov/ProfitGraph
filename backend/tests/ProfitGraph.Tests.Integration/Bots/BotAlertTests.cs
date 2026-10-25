using Dapper;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Alerts recorded from outbox events (stage 5.9, ADR 0018): one per degradation, kill, risk rejection and the start of
///     an unavailable-venue streak, for the bot's owner; other rejections raise none.
/// </summary>
[NonParallelizable]
public sealed class BotAlertTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task EachAlertingEventRaisesOneAlertForTheOwner()
    {
        using IHost writer = BotTestHost.Create();
        Bot bot = await AddRunningBot(writer);
        await ChangeBot(writer, bot.Id, stored => stored.MarkExchangeUnavailable(unavailable: true, BotSeed.Now));
        await ChangeBot(writer, bot.Id, stored => stored.MarkExchangeUnavailable(unavailable: false, BotSeed.Now));
        await ChangeBot(writer, bot.Id, stored => stored.MarkExchangeUnavailable(unavailable: true, BotSeed.Now.AddMinutes(1)));
        await ChangeBot(writer, bot.Id, stored => stored.MarkExchangeUnavailable(unavailable: true, BotSeed.Now.AddMinutes(2)));
        await ChangeBot(writer, bot.Id, stored => stored.Degrade(BotStatusReason.UnresolvedOrder, BotSeed.Now.AddMinutes(3)));
        await AddRejected(writer, bot, engineOrderId: 1, RejectionReason.RiskLimit);
        await AddRejected(writer, bot, engineOrderId: 2, RejectionReason.KillSwitch);
        await ChangeBot(writer, bot.Id, stored => stored.Kill(BotStatusReason.Killed, BotSeed.Now.AddMinutes(5)));

        using IHost worker = BotTestHost.Create(services => services.AddOutboxDispatcher());
        await worker.StartAsync();
        await WaitForAlerts(bot, count: 5);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        List<(string Kind, string? Reason, Guid OwnerId)> alerts =
        [
            .. await connection.QueryAsync<(string, string?, Guid)>(
                "SELECT kind, reason, owner_id FROM bot_alerts WHERE bot_id = @id ORDER BY created_at",
                new { id = bot.Id.Value })
        ];
        alerts.Select(alert => (alert.Kind, alert.Reason))
            .ShouldBe(
            [
                ("ExchangeUnavailable", null),
                ("ExchangeUnavailable", null),
                ("Degraded", "UnresolvedOrder"),
                ("RiskRejection", "RiskLimit"),
                ("Killed", "Killed")
            ]);
        alerts.ShouldAllBe(alert => alert.OwnerId == bot.OwnerId.Value);
    }

    private static async Task<Bot> AddRunningBot(IHost host)
    {
        Bot bot = await BotSeed.NewBot();
        bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
        bot.ClearDomainEvents();
        await InTransaction(host, services => services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None));

        return bot;
    }

    private static Task ChangeBot(IHost host, BotId id, Action<Bot> change)
    {
        return InTransaction(
            host,
            async services =>
            {
                var bots = services.GetRequiredService<IBotRepository>();
                Bot stored = (await bots.GetForUpdate(id, CancellationToken.None)).ShouldNotBeNull();
                change(stored);
                await bots.Update(stored, CancellationToken.None);
            });
    }

    private static Task AddRejected(IHost host, Bot bot, long engineOrderId, RejectionReason reason)
    {
        DateTimeOffset at = BotSeed.Now.AddMinutes(4);
        OrderIntent intent = OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(engineOrderId), new ClientOrderId($"pg-{engineOrderId}"), at);
        intent.Claim(at);
        intent.Reject(new OrderIntentRejection(reason, "invented"), at);

        return InTransaction(host, services => services.GetRequiredService<IOrderIntentRepository>().TryAdd(intent, CancellationToken.None));
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

    private static async Task WaitForAlerts(Bot bot, int count)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        DateTime deadline = DateTime.UtcNow + Timeout;

        while (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_alerts WHERE bot_id = @id", new { id = bot.Id.Value }) < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The alerts were not recorded in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }
}