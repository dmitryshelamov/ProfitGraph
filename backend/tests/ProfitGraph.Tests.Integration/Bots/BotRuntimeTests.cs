using Microsoft.Extensions.Hosting;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Bots run in the Worker's live role against a scripted gateway (stage 5.5): one start, restarts without
///     <c>OnStart</c>, takeover by another Worker, live candle roots, failures and a process where trading is disabled.
/// </summary>
[NonParallelizable]
public sealed class BotRuntimeTests
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
    public async Task FirstStartEntersOnceAndRuns()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");
        await worker.StopAsync();

        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        BotCheckpoint checkpoint = (await RuntimeScenario.Checkpoint(worker, bot.Id)).ShouldNotBeNull();
        IReadOnlyList<OrderIntent> intents = await RuntimeScenario.Intents(worker, bot.Id);
        stored.HasRun.ShouldBeTrue();
        checkpoint.Engine.Started.ShouldBeTrue();
        intents.Count.ShouldBe(1);
        intents[0].Kind.ShouldBe(OrderIntentKind.Submit);
        intents[0].Order!.Type.ShouldBe(OrderType.Market);
        intents[0].ClientOrderId.Value.ShouldBe($"pgtest-{bot.Id.Value:N}-{intents[0].EngineOrderId}");
    }

    [Test]
    public async Task RestartResumesWithoutAnotherStart()
    {
        var scenario = new RuntimeScenario();
        Bot bot;
        long eventsBefore;

        using (IHost first = scenario.Worker())
        {
            bot = await RuntimeScenario.RunningLadder(first);
            await first.StartAsync();
            await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(first, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");
            await first.StopAsync();
            eventsBefore = (await RuntimeScenario.Checkpoint(first, bot.Id))!.Engine.NextEventId;
        }

        using IHost second = scenario.Worker();
        await second.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Lease(bot.Id))?.Epoch == 2, "the second Worker takes the lease");
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(second, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs again");
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Checkpoint(second, bot.Id))!.Engine.NextEventId > eventsBefore, "the resume is saved");
        await second.StopAsync();

        BotCheckpoint checkpoint = (await RuntimeScenario.Checkpoint(second, bot.Id))!;
        (await RuntimeScenario.Intents(second, bot.Id)).Count.ShouldBe(1);
        checkpoint.Engine.Account.NextOrderId.ShouldBe(1);
    }

    [Test]
    public async Task AnotherWorkerTakesTheBotOnlyAfterTheFirstLetsGo()
    {
        var scenario = new RuntimeScenario();
        using IHost first = scenario.Worker();
        using IHost second = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(first);
        await first.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(first, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the first Worker runs the bot");
        (Guid Holder, long Epoch, DateTime ExpiresAt) firstLease = (await RuntimeScenario.Lease(bot.Id))!.Value;

        await second.StartAsync();
        await Task.Delay(RuntimeScenario.LeaseDuration);
        (await RuntimeScenario.Lease(bot.Id))!.Value.Holder.ShouldBe(firstLease.Holder);

        await first.StopAsync();
        await RuntimeScenario.Until(
            async () => await RuntimeScenario.Lease(bot.Id) is { Epoch: 2 } lease && lease.Holder != firstLease.Holder,
            "the second Worker takes over");
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(second, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the second Worker runs the bot");
        await second.StopAsync();

        (await RuntimeScenario.Intents(second, bot.Id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task LiveClosedCandleIsCommittedAsARoot()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Candles.Subscribers > 0), "the bot subscribes to live candles");
        Candle last = (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle;
        Candle next = FlatHistorySource.Candle(last.OpenTime.AddHours(1));

        scenario.Candles.Push(next);
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle == next, "the live candle is committed");
        await worker.StopAsync();

        (await RuntimeScenario.Read(worker, bot.Id)).RestartAttempts.ShouldBe(0);
    }

    [Test]
    public async Task FailedInputDegradesTheBotForAnAutomaticRestart()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Candles.Subscribers > 0), "the bot subscribes to live candles");

        scenario.Candles.Fail(new InvalidDataException("The test feed broke."));
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).IsDegraded, "the bot degrades");
        await worker.StopAsync();

        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        stored.StatusReason.ShouldBe(BotStatusReason.RuntimeFailure);
        stored.NextRestartAt.ShouldNotBeNull();
    }

    [Test]
    public async Task WithoutAGatewayTheBotDegradesAndSendsNothing()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker(withGateway: false);
        Bot bot = await RuntimeScenario.RunningLadder(worker);

        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).IsDegraded, "the bot degrades");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync();

        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        stored.StatusReason.ShouldBe(BotStatusReason.NoGateway);
        (await RuntimeScenario.Checkpoint(worker, bot.Id)).ShouldBeNull();
        (await RuntimeScenario.Intents(worker, bot.Id)).ShouldBeEmpty();
        (await RuntimeScenario.Lease(bot.Id))!.Value.Epoch.ShouldBe(1);
    }

    [Test]
    public async Task StoppedBotReleasesItsLeaseAndReportsStopped()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");

        await BotStore.Change(worker, bot.Id, stored => stored.Stop(DateTimeOffset.UtcNow));
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Stopped, "the runtime stops");
        await worker.StopAsync();

        (await RuntimeScenario.Lease(bot.Id))!.Value.ExpiresAt.ShouldBeLessThan(DateTime.UtcNow);
    }
}