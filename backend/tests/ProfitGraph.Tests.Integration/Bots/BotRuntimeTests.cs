using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.MarketData.Contracts;
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
    public async Task ALiveCandleAfterAMarketClosure_IsCommitted_AndAnUncoveredGapFails()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Candles.Subscribers > 0), "the bot subscribes to live candles");
        Candle last = (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle;
        scenario.History.Closed.Add(new CandleRangeInternal(last.OpenTime.AddHours(1), last.OpenTime.AddHours(9)));
        Candle morning = FlatHistorySource.Candle(last.OpenTime.AddHours(9));

        scenario.Candles.Push(morning);
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle == morning,
            "the candle after the night is committed");
        scenario.History.Closed.Clear();
        Candle afterHole = FlatHistorySource.Candle(morning.OpenTime.AddHours(3));
        scenario.History.Closed.Add(new CandleRangeInternal(morning.OpenTime.AddHours(2), morning.OpenTime.AddHours(3)));
        scenario.Candles.Push(afterHole);
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle == afterHole,
            "a missed candle from history and the closure before the next live candle are committed");
        await worker.StopAsync();

        (await RuntimeScenario.Read(worker, bot.Id)).RestartAttempts.ShouldBe(0);
    }

    [Test]
    public async Task FailedInputDegradesTheBotForAnAutomaticRestart()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker(withOutbox: true);
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Candles.Subscribers > 0), "the bot subscribes to live candles");

        scenario.Candles.Fail(new InvalidDataException("The test feed broke."));
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).IsDegraded, "the bot degrades");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Feed(worker, bot.Id)).Any(entry => entry.Kind == BotFeedKind.Degraded),
            "the feed reports it");
        await worker.StopAsync();

        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        stored.StatusReason.ShouldBe(BotStatusReason.RuntimeFailure);
        stored.NextRestartAt.ShouldNotBeNull();

        // The owner sees the type chain and our own message, never the inner exception's text.
        (await RuntimeScenario.Feed(worker, bot.Id)).Single(entry => entry.Kind == BotFeedKind.Degraded).Details["detail"]
            .ShouldBe("InvalidOperationException: A live input of the bot failed. <- InvalidDataException");
    }

    [Test]
    public async Task ARestatedLastCandleOfAVenueWithFinalCandlesStopsTheBotWithoutSpendingRestarts()
    {
        var scenario = new RuntimeScenario();
        Bot bot;
        Candle stored;

        using (IHost first = scenario.Worker())
        {
            bot = await RuntimeScenario.RunningLadder(first);
            await first.StartAsync();
            await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(first, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");
            await first.StopAsync();
            stored = (await RuntimeScenario.Checkpoint(first, bot.Id))!.Engine.LastClosedCandle;
        }

        scenario.History.Volume = 2m;
        using IHost second = scenario.Worker(withOutbox: true);
        await second.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(second, bot.Id)).IsDegraded, "the bot degrades");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Feed(second, bot.Id)).Any(entry => entry.Kind == BotFeedKind.Degraded),
            "the feed reports it");
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await second.StopAsync();

        Bot degraded = await RuntimeScenario.Read(second, bot.Id);
        degraded.ShouldSatisfyAllConditions(
            () => degraded.StatusReason.ShouldBe(BotStatusReason.HistoryDataCorrection),
            () => degraded.RestartAttempts.ShouldBe(0),
            () => degraded.NextRestartAt.ShouldBeNull(),
            () => degraded.CanAcquireLease(DateTimeOffset.UtcNow.AddDays(1)).ShouldBeFalse());
        (await RuntimeScenario.Checkpoint(second, bot.Id))!.Engine.LastClosedCandle.ShouldBe(stored);
        (await RuntimeScenario.Lease(bot.Id))!.Value.Epoch.ShouldBe(2);
        BotFeedEntry entry = (await RuntimeScenario.Feed(second, bot.Id)).Single(candidate => candidate.Kind == BotFeedKind.Degraded);
        entry.Details["reason"].ShouldBe("HistoryDataCorrection");
        entry.Details["detail"].ShouldBe("OrderGatewaySafetyException: The venue changed a closed candle the bot had already processed.");
    }

    [Test]
    public async Task ARestatedLastCandleOfAVenueThatRestatesIsAcceptedAndRecordedInTheFeed()
    {
        var scenario = new RuntimeScenario();
        scenario.History.RestatesClosedCandles = true;
        Bot bot;
        Candle stored;

        using (IHost first = scenario.Worker())
        {
            bot = await RuntimeScenario.RunningLadder(first);
            await first.StartAsync();
            await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(first, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs");
            await first.StopAsync();
            stored = (await RuntimeScenario.Checkpoint(first, bot.Id))!.Engine.LastClosedCandle;
        }

        scenario.History.Volume = 2m;
        using IHost second = scenario.Worker(withOutbox: true);
        await second.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(second, bot.Id))!.Engine.LastClosedCandle == stored with { Volume = 2m },
            "the restated candle is committed");
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(second, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs again");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Feed(second, bot.Id)).Any(entry => entry.Kind == BotFeedKind.HistoryRestated),
            "the feed reports it");
        await second.StopAsync();

        BotFeedEntry entry = (await RuntimeScenario.Feed(second, bot.Id)).Single(entry => entry.Kind == BotFeedKind.HistoryRestated);
        entry.Details["openTime"].ShouldBe(stored.OpenTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        (await RuntimeScenario.Read(second, bot.Id)).ShouldSatisfyAllConditions(
            bot2 => bot2.IsDegraded.ShouldBeFalse(),
            bot2 => bot2.RestartAttempts.ShouldBe(0));
        (await RuntimeScenario.Intents(second, bot.Id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task ALiveCandleAfterRestartsStartsTheRestartStreakOver()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        DateTimeOffset at = DateTimeOffset.UtcNow.AddHours(-1);

        // Three failed restarts in the past, the third restart's failure waiting for its time.
        await BotStore.Change(
            worker,
            bot.Id,
            stored =>
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    stored.RecordRuntimeFailure(at);
                    at = stored.NextRestartAt!.Value;
                    stored.RecordLeaseAcquired(at);
                    stored.MarkStarting(at);
                }

                stored.RecordRuntimeFailure(at);
            });
        await worker.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs again");
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Candles.Subscribers > 0), "the bot subscribes to live candles");
        (await RuntimeScenario.Read(worker, bot.Id)).RestartAttempts.ShouldBe(4);
        Candle last = (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle;
        Candle next = FlatHistorySource.Candle(last.OpenTime.AddHours(1));

        scenario.Candles.Push(next);
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle == next, "the live candle is committed");
        await worker.StopAsync();

        (await RuntimeScenario.Read(worker, bot.Id)).ShouldSatisfyAllConditions(
            stored => stored.RestartAttempts.ShouldBe(0),
            stored => stored.NextRestartAt.ShouldBeNull());
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