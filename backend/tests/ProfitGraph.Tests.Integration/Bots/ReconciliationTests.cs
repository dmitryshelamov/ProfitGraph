using Dapper;

using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Reconciliation of an opened bot with the scripted venue before its submit gate opens (stage 5.8): no submit leaves
///     first, an unavailable venue is asked again, a safety reason degrades the bot and an order the venue knows resolves
///     its intent.
/// </summary>
[NonParallelizable]
public sealed class ReconciliationTests
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
    public async Task BotIsReconciledBeforeItsFirstSubmitLeaves()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(worker, bot.Id) is [{ Status: OrderIntentStatus.Sent }], "the submit is sent");
        await worker.StopAsync();

        scenario.Gateway.Reconciles.ShouldBe(1);
        scenario.Gateway.PlacesBeforeLastReconcile.ShouldBe(0);
        scenario.Gateway.Places.ShouldBe(1);
    }

    [Test]
    public async Task UnavailableVenueIsMarkedAndAskedAgain()
    {
        var scenario = new RuntimeScenario();
        scenario.Gateway.FailReconcile(count: 2);
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Read(worker, bot.Id)).Diagnostics.HasFlag(BotDiagnostics.ExchangeUnavailable),
            "the venue is marked unavailable");
        await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(worker, bot.Id) is [{ Status: OrderIntentStatus.Sent }], "the submit is sent");
        await worker.StopAsync();

        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        stored.Diagnostics.HasFlag(BotDiagnostics.ExchangeUnavailable).ShouldBeFalse();
        scenario.Gateway.Reconciles.ShouldBe(3);
    }

    [Test]
    public async Task SafetyReasonDegradesTheBotBeforeAnySubmit()
    {
        var scenario = new RuntimeScenario { Gateway = { ReconcileDegradation = BotStatusReason.ForeignOrder } };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).StatusReason == BotStatusReason.ForeignOrder, "the bot degrades");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync();

        (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus.ShouldBe(BotRuntimeStatus.Degraded);
        scenario.Gateway.Places.ShouldBe(0);
        scenario.Gateway.Reconciles.ShouldBe(1);
    }

    [Test]
    public async Task FailedSubmitTheVenueKnowsIsResolvedWhenTheBotOpensAgain()
    {
        var scenario = new RuntimeScenario { Gateway = { FailEveryPlace = true } };
        Bot bot;

        using (IHost first = scenario.Worker())
        {
            bot = await RuntimeScenario.RunningLadder(first);
            await first.StartAsync();
            await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(first, bot.Id) is [{ PlaceStartedAt: not null }], "the place starts");
            await first.StopAsync();
        }

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE bot_order_intents SET status = 'Failed', next_attempt_at = NULL, claimed_at = NULL WHERE bot_id = @id",
                new { id = bot.Id.Value });
        }

        scenario.Gateway.FailEveryPlace = false;
        using IHost second = scenario.Worker();
        await second.StartAsync();
        await RuntimeScenario.Until(async () => await RuntimeScenario.Intents(second, bot.Id) is [{ Status: OrderIntentStatus.Sent }], "the intent is resolved");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(second, bot.Id))!.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the venue's state reaches the engine");
        await second.StopAsync();

        scenario.Gateway.Places.ShouldBe(1);
    }

    [Test]
    public async Task StoppedBotEndsWhileTheVenueIsUnavailable()
    {
        var scenario = new RuntimeScenario();
        scenario.Gateway.FailReconcile(count: 1_000);
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Read(worker, bot.Id)).Diagnostics.HasFlag(BotDiagnostics.ExchangeUnavailable),
            "the venue is marked unavailable");
        await BotStore.Change(worker, bot.Id, stored => stored.Stop(DateTimeOffset.UtcNow));
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Stopped, "the runner ends");
        await worker.StopAsync();

        scenario.Gateway.Places.ShouldBe(0);
    }
}