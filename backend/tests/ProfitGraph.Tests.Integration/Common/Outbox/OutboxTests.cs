using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Common.Outbox.PruneOutbox;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Common.Outbox;
using ProfitGraph.Tests.Integration.Bots;

namespace ProfitGraph.Tests.Integration.Common.Outbox;

/// <summary>
///     Domain events are stored with the state change that raised them and delivered by the Worker once (ADR 0004); a message
///     that keeps failing is parked and delivered messages are deleted after the retention (ADR 0045).
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

    [TearDown]
    public async Task DeleteParked()
    {
        // A parked message would leave readiness Degraded for the tests that follow.
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("DELETE FROM outbox_messages WHERE parked_at IS NOT NULL");
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
    public async Task DeliveryThatKeepsFailingIsParkedAndMakesReadinessDegraded()
    {
        RecordingStopHandler.FailNext(count: 1000);
        using IHost writer = BotTestHost.Create();
        Bot bot = await StoppedRunningBot(writer);

        using IHost worker = await StartWorker(
            new Dictionary<string, string?>
            {
                ["Outbox:MaxAttempts"] = "2",
                ["Outbox:MaxRetryDelay"] = TimeSpan.FromMilliseconds(50).ToString()
            });
        await WaitUntil(bot, "parked_at IS NOT NULL");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        var row = await connection.QuerySingleAsync<(int Attempts, string? LastError, DateTime? DeliveredAt)>(
            "SELECT attempts, last_error, delivered_at FROM outbox_messages WHERE type = 'BotStopped' AND payload->>'BotId' = @id",
            new { id = bot.Id.Value.ToString() });
        row.Attempts.ShouldBe(2);
        row.LastError.ShouldBe(nameof(InvalidOperationException));
        row.DeliveredAt.ShouldBeNull();
        (await Effects(bot)).ShouldBe(0);
        HealthReportEntry parked = await OutboxHealth();
        parked.Status.ShouldBe(HealthStatus.Degraded);
        parked.Description.ShouldNotBeNull().ShouldContain("parked");

        await DeleteParked();

        (await OutboxHealth()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Test]
    public async Task PruneDeletesOneBatchOfDeliveredMessagesOlderThanTheRetention()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid[] expired = await InsertMessages(count: 3, now.AddDays(-20), delivered: true);
        Guid[] kept =
        [
            .. await InsertMessages(count: 1, now.AddDays(-1), delivered: true),
            .. await InsertMessages(count: 1, now.AddDays(-20), delivered: false)
        ];
        using IHost host = BotTestHost.Create();
        var command = new PruneOutboxCommand(TimeSpan.FromDays(14), BatchSize: 2);

        try
        {
            (await Prune(host, command)).ShouldBe(2);
            (await Prune(host, command)).ShouldBe(1);
            (await Prune(host, command)).ShouldBe(0);

            (await Existing(expired)).ShouldBeEmpty();
            (await Existing(kept)).ShouldBe(kept, ignoreOrder: true);
        }
        finally
        {
            await Delete(kept);
        }
    }

    [Test]
    public async Task WorkerDeletesEveryExpiredBatchAndKeepsParkedMessages()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid[] expired = await InsertMessages(count: 5, now.AddDays(-20), delivered: true);
        Guid[] parked = await InsertMessages(count: 1, now.AddDays(-20), delivered: false);

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("UPDATE outbox_messages SET parked_at = created_at WHERE id = ANY(@parked)", new { parked });

        using IHost worker = await StartWorker(new Dictionary<string, string?> { ["Outbox:RetentionBatchSize"] = "2" });
        DateTime deadline = DateTime.UtcNow + Timeout;

        while ((await Existing(expired)).Length > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        await worker.StopAsync();

        (await Existing(expired)).ShouldBeEmpty();
        (await Existing(parked)).ShouldBe(parked);
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

    private static async Task<IHost> StartWorker(IReadOnlyDictionary<string, string?>? settings = null)
    {
        IHost worker = BotTestHost.Create(
            services =>
            {
                services.AddOutboxDispatcher();
                services.AddTransient<INotificationHandler<BotStopped>, RecordingStopHandler>();
            },
            settings: settings);
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

    private static Task WaitUntilDelivered(Bot bot)
    {
        return WaitUntil(bot, "delivered_at IS NOT NULL");
    }

    private static async Task WaitUntil(Bot bot, string condition)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        DateTime deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await connection.ExecuteScalarAsync<bool>(
                    $"SELECT EXISTS (SELECT 1 FROM outbox_messages WHERE type = 'BotStopped' AND payload->>'BotId' = @id AND {condition})",
                    new { id = bot.Id.Value.ToString() }))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Fail($"The outbox event did not reach {condition} in time.");
    }

    private async Task<HealthReportEntry> OutboxHealth()
    {
        HealthReport report = await _api.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(check => check.Name == OutboxHealthCheck.Name);

        return report.Entries[OutboxHealthCheck.Name];
    }

    private static async Task<int> Prune(IHost host, PruneOutboxCommand command)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    /// <summary>
    ///     Stores messages of a type no handler takes, created at <paramref name="at" /> and, when delivered, delivered then.
    /// </summary>
    private static async Task<Guid[]> InsertMessages(int count, DateTimeOffset at, bool delivered)
    {
        Guid[] ids = [.. Enumerable.Range(start: 0, count).Select(_ => Guid.NewGuid())];
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync(
            sql: """
                 INSERT INTO outbox_messages (id, type, payload, created_at, delivered_at, next_attempt_at)
                 SELECT id, 'RetentionTestEvent', '{}', @at, CASE WHEN @delivered THEN @at END, 'infinity'
                 FROM unnest(@ids) AS id
                 """,
            new { ids, at, delivered });

        return ids;
    }

    private static async Task<Guid[]> Existing(Guid[] ids)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return [.. await connection.QueryAsync<Guid>("SELECT id FROM outbox_messages WHERE id = ANY(@ids)", new { ids })];
    }

    private static async Task Delete(Guid[] ids)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("DELETE FROM outbox_messages WHERE id = ANY(@ids)", new { ids });
    }
}