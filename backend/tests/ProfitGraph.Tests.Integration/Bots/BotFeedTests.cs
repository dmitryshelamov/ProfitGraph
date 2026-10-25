using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Bots.PruneBotFeed;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The bot's event feed from outbox events (stage 5.12, ADR 0021): one entry per fact in stored order, and entries older
///     than 90 days removed.
/// </summary>
[NonParallelizable]
public sealed class BotFeedTests
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
    public async Task EventsBecomeFeedEntriesInTheirOrder()
    {
        using IHost writer = BotTestHost.Create();
        Bot bot = await BotSeed.NewBot();
        bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
        await InTransaction(writer, services => services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None));
        BotFill fill = BotFill.Record(
            BotFillId.New(),
            bot.Id,
            engineOrderId: 1,
            OrderSide.Buy,
            "entry",
            grossBase: 0.10000000m,
            grossQuote: 10.00000000m,
            fee: 0m,
            cumulativeGrossBase: 0.10000000m,
            BotSeed.Now);
        await InTransaction(writer, services => services.GetRequiredService<IBotFillRepository>().TryAdd(fill, CancellationToken.None));
        await ChangeBot(writer, bot.Id, stored => stored.Kill(BotStatusReason.Killed, BotSeed.Now.AddMinutes(1)));

        using IHost worker = BotTestHost.Create(services => services.AddOutboxDispatcher());
        await worker.StartAsync();
        await WaitForEntries(bot, count: 3);
        await worker.StopAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        List<(string Kind, string Details)> entries =
        [
            .. await connection.QueryAsync<(string, string)>("SELECT kind, details::text FROM bot_events WHERE bot_id = @id ORDER BY sequence", new { id = bot.Id.Value })
        ];
        entries.Select(entry => entry.Kind).ShouldBe(["Started", "Fill", "Killed"]);
        entries[1].Details.ShouldContain("\"grossBase\": \"0.1\"");
        entries[1].Details.ShouldContain("\"grossQuote\": \"10\"");
        entries[2].Details.ShouldContain("\"reason\": \"Killed\"");
    }

    [Test]
    public async Task EntriesOlderThanTheRetentionAreRemoved()
    {
        using IHost host = BotTestHost.Create();
        Bot bot = await BotSeed.NewBot();
        await InTransaction(host, services => services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await InTransaction(
            host,
            async services =>
            {
                var feed = services.GetRequiredService<IBotFeedRepository>();
                await feed.Add(
                    BotFeedEntry.Record(BotFeedEntryId.New(), bot.Id, BotFeedKind.Started, new Dictionary<string, string>(), now.AddDays(-91)),
                    CancellationToken.None);
                await feed.Add(BotFeedEntry.Record(BotFeedEntryId.New(), bot.Id, BotFeedKind.Stopped, new Dictionary<string, string>(), now), CancellationToken.None);
            });

        using IServiceScope scope = host.Services.CreateScope();
        int removed = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PruneBotFeedCommand());

        removed.ShouldBeGreaterThanOrEqualTo(1);
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.QueryAsync<string>("SELECT kind FROM bot_events WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe(["Stopped"]);
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

    private static async Task WaitForEntries(Bot bot, int count)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        DateTime deadline = DateTime.UtcNow + Timeout;

        while (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_events WHERE bot_id = @id", new { id = bot.Id.Value }) < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The feed entries were not recorded in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }
}