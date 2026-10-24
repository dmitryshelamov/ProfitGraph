using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Tests.Integration.Bots;

namespace ProfitGraph.Tests.Integration.Common.Outbox;

/// <summary>
///     Domain events are stored with the state change that raised them and delivered by the Worker once (ADR 0004).
/// </summary>
[NonParallelizable]
public sealed class OutboxTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync(RecordingStopHandler.CreateTable);
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [SetUp]
    public void Reset()
    {
        RecordingStopHandler.FailNext(count: 0);
    }

    [Test]
    public async Task EventIsStoredWithTheCommit()
    {
        using IHost host = BotTestHost.Create();
        Bot bot = await StoppedRunningBot(host);

        (await Messages(bot)).ShouldBe(1);
    }

    [Test]
    public async Task RolledBackChangeStoresNoEvent()
    {
        using IHost host = BotTestHost.Create();
        Bot bot = await AddRunningBot(host);

        await Should.ThrowAsync<InvalidOperationException>(() => ChangeBot(
            host,
            bot.Id,
            stored =>
            {
                stored.Stop(BotSeed.Now);

                throw new InvalidOperationException("Roll back.");
            }));

        (await Messages(bot)).ShouldBe(0);
    }

    [Test]
    public async Task WorkerDeliversAnEventOnce()
    {
        using IHost writer = BotTestHost.Create();
        Bot bot = await StoppedRunningBot(writer);

        using IHost worker = await StartWorker();
        await WaitUntilDelivered(bot);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync();

        (await Effects(bot)).ShouldBe(1);
    }

    [Test]
    public async Task FailedDeliveryIsRetriedAndTakesEffectOnce()
    {
        RecordingStopHandler.FailNext(count: 1);
        using IHost writer = BotTestHost.Create();
        Bot bot = await StoppedRunningBot(writer);

        using IHost worker = await StartWorker();
        await WaitUntilDelivered(bot);
        await worker.StopAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<int>(
                "SELECT attempts FROM outbox_messages WHERE type = 'BotStopped' AND payload->>'BotId' = @id",
                new { id = bot.Id.Value.ToString() }))
            .ShouldBe(1);
        (await Effects(bot)).ShouldBe(1);
    }

    [Test]
    public async Task EventStoredWhileNoWorkerRunsIsDeliveredAfterItStarts()
    {
        using IHost writer = BotTestHost.Create();
        Bot bot = await StoppedRunningBot(writer);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        (await Effects(bot)).ShouldBe(0);

        using IHost worker = await StartWorker();
        await WaitUntilDelivered(bot);
        await worker.StopAsync();

        (await Effects(bot)).ShouldBe(1);
    }

    [Test]
    public async Task UnknownEventTypeIsKeptAndDoesNotBlockOthers()
    {
        var unknown = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync(
                "INSERT INTO outbox_messages (id, type, payload, created_at) VALUES (@unknown, 'NoSuchEvent', '{}', now())",
                new { unknown });
        }

        using IHost writer = BotTestHost.Create();
        Bot bot = await StoppedRunningBot(writer);
        using IHost worker = await StartWorker();
        await WaitUntilDelivered(bot);
        await worker.StopAsync();

        await using var check = new NpgsqlConnection(TestDatabase.ConnectionString);
        var row = await check.QuerySingleAsync<(DateTime? DeliveredAt, string? LastError)>(
            "SELECT delivered_at, last_error FROM outbox_messages WHERE id = @unknown",
            new { unknown });
        row.DeliveredAt.ShouldBeNull();
        row.LastError.ShouldBe("unknown_event_type");
    }

    [Test]
    public void EventTypesSerializeValueObjectsAsValues()
    {
        var types = new OutboxEventTypes();
        var stopped = new BotStopped(BotId.New(), BotSeed.Now);

        string json = types.Serialize(stopped);

        json.ShouldContain($"\"BotId\":\"{stopped.BotId.Value}\"");
        types.Deserialize(types.NameOf(stopped), json).ShouldBe(stopped);
    }

    private static async Task<IHost> StartWorker()
    {
        IHost worker = BotTestHost.Create(services =>
        {
            services.AddOutboxDispatcher();
            services.AddTransient<INotificationHandler<BotStopped>, RecordingStopHandler>();
        });
        await worker.StartAsync();

        return worker;
    }

    private static async Task<Bot> StoppedRunningBot(IHost host)
    {
        Bot bot = await AddRunningBot(host);
        await ChangeBot(host, bot.Id, stored => stored.Stop(BotSeed.Now));

        return bot;
    }

    private static async Task<Bot> AddRunningBot(IHost host)
    {
        Bot bot = await BotSeed.NewBot();
        bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
        bot.ClearDomainEvents();
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return bot;
    }

    private static async Task ChangeBot(IHost host, BotId id, Action<Bot> change)
    {
        using IServiceScope scope = host.Services.CreateScope();
        var bots = scope.ServiceProvider.GetRequiredService<IBotRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    Bot stored = (await bots.GetForUpdate(id, CancellationToken.None)).ShouldNotBeNull();
                    change(stored);
                    await bots.Update(stored, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }

    private static async Task<int> Messages(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM outbox_messages WHERE type = 'BotStopped' AND payload->>'BotId' = @id",
            new { id = bot.Id.Value.ToString() });
    }

    private static async Task<int> Effects(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM outbox_test_effects WHERE bot_id = @id", new { id = bot.Id.Value });
    }

    private static async Task WaitUntilDelivered(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        DateTime deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await connection.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS (SELECT 1 FROM outbox_messages WHERE type = 'BotStopped' AND payload->>'BotId' = @id AND delivered_at IS NOT NULL)",
                    new { id = bot.Id.Value.ToString() }))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Fail("The outbox event was not delivered in time.");
    }
}