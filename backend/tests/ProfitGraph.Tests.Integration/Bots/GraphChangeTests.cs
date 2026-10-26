using System.Text.Json;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.GetStrategy;
using ProfitGraph.Application.Strategies.SaveStrategy;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Tests.Integration.Common;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Running bots follow a newly saved strategy version on their next root (stage 5.10, ADR 0019): an order of a removed
///     block is cancelled once, and a version the bot cannot take leaves it on its version with one rejection.
/// </summary>
[NonParallelizable]
public sealed class GraphChangeTests
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
    public async Task BotsSwitchOnTheirNextRootAndCancelTheOrderOfARemovedBlockOnce()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        IReadOnlyList<Bot> bots = await RuntimeScenario.RunningLadders(worker, count: 2);
        await worker.StartAsync();
        await EntriesAccepted(worker, bots);

        StrategyVersionId next = await Save(worker, bots[0], definition => Renamed(definition, "entry", "entryMoved"));
        await PushNextCandle(worker, scenario, bots[0]);
        await RuntimeScenario.Until(
            async () => await Versions(worker, bots) == next && scenario.Gateway.Cancels.Count == 2,
            "both bots switch and cancel the removed block's order");
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync();

        scenario.Gateway.Cancels.ShouldBe(
            [.. bots.Select(bot => scenario.Gateway.ClientOrderIdFor(bot.Id, engineOrderId: 1))],
            ignoreOrder: true);

        foreach (Bot bot in bots)
        {
            (await RuntimeScenario.Intents(worker, bot.Id)).Count(intent => intent.Kind == OrderIntentKind.Cancel).ShouldBe(1);
        }
    }

    [Test]
    public async Task VersionTheBotCannotTakeLeavesItOnItsVersionAndIsRejectedOnce()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        IReadOnlyList<Bot> bots = await RuntimeScenario.RunningLadders(worker, count: 1);
        Bot bot = bots[0];
        await worker.StartAsync();
        await EntriesAccepted(worker, bots);

        StrategyVersionId next = await Save(
            worker,
            bot,
            definition => definition with
            {
                Parameters = [.. definition.Parameters, new StrategyParameterInternal("extra", ValueType.Scalar, DefaultValue: 1m)]
            });
        await PushNextCandle(worker, scenario, bot);
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(worker, bot.Id)).RejectedVersionId == next, "the version is rejected");
        await PushNextCandle(worker, scenario, bot);
        await worker.StopAsync();

        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        stored.StrategyVersionId.ShouldBe(bot.StrategyVersionId);
        stored.Diagnostics.HasFlag(BotDiagnostics.GraphChangeRejected).ShouldBeTrue();
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM outbox_messages WHERE type = 'BotGraphChangeRejected' AND payload->>'BotId' = @id",
                new { id = bot.Id.Value.ToString() }))
            .ShouldBe(1);
    }

    [Test]
    public async Task ASavedVersionIsFollowedWithoutWaitingForACandle()
    {
        var scenario = new RuntimeScenario();
        using IHost worker = scenario.Worker();
        IReadOnlyList<Bot> bots = await RuntimeScenario.RunningLadders(worker, count: 1);
        Bot bot = bots[0];
        await worker.StartAsync();
        await EntriesAccepted(worker, bots);

        StrategyVersionId next = await Save(worker, bot, definition => Renamed(definition, "entry", "entryMoved"));
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Read(worker, bot.Id)).StrategyVersionId == next && scenario.Gateway.Cancels.Count == 1,
            "the bot switches with no new candle and cancels the removed block's order");
        await worker.StopAsync();
    }

    [Test]
    public async Task AStoredRunTakesTheBotsNewValuesAndAllocationOnItsNextOpen()
    {
        var scenario = new RuntimeScenario();
        Bot bot;

        using (IHost first = scenario.Worker())
        {
            bot = (await RuntimeScenario.RunningLadders(first, count: 1))[0];
            await first.StartAsync();
            await EntriesAccepted(first, [bot]);
            await first.StopAsync();
        }

        using IHost reader = scenario.Worker();
        BotCheckpoint before = (await RuntimeScenario.Checkpoint(reader, bot.Id))!;

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE bots SET allocation_cap = 250, parameters = CAST(@parameters AS jsonb) WHERE id = @id",
                new
                {
                    id = bot.Id.Value,
                    parameters = JsonSerializer.Serialize(bot.Parameters.Values.ToDictionary(pair => pair.Key, pair => pair.Key == "drop" ? 0.07m : pair.Value))
                });
        }

        using IHost second = scenario.Worker();
        await second.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(second, bot.Id))?.Engine.Account.StartingCapital == 250m,
            "the run takes the new allocation");
        await second.StopAsync();

        BotCheckpoint after = (await RuntimeScenario.Checkpoint(second, bot.Id))!;
        after.AppliedParameters.Values["drop"].ShouldBe(0.07m);
        after.Engine.Account.QuoteTotal.ShouldBe(before.Engine.Account.QuoteTotal + 250m - before.Engine.Account.StartingCapital);
        after.Engine.Account.CostBasis.ShouldBe(before.Engine.Account.CostBasis);
        after.Engine.Account.Orders.Select(order => order.Id).ShouldBe(before.Engine.Account.Orders.Select(order => order.Id));
    }

    [Test]
    public async Task AnAllocationBelowWhatTheRunReservedLeavesTheRunAsItWasAndIsRejected()
    {
        var scenario = new RuntimeScenario();
        Bot bot;

        using (IHost first = scenario.Worker())
        {
            bot = (await RuntimeScenario.RunningLadders(first, count: 1))[0];
            await first.StartAsync();
            await EntriesAccepted(first, [bot]);
            await first.StopAsync();
        }

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE bots SET allocation_cap = 5 WHERE id = @id", new { id = bot.Id.Value });
        }

        using IHost second = scenario.Worker();
        await second.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Read(second, bot.Id)).Diagnostics.HasFlag(BotDiagnostics.GraphChangeRejected),
            "the settings are refused");
        await second.StopAsync();

        (await RuntimeScenario.Checkpoint(second, bot.Id))!.Engine.Account.StartingCapital.ShouldBe(100m);
    }

    private static async Task EntriesAccepted(IHost worker, IReadOnlyList<Bot> bots)
    {
        foreach (Bot bot in bots)
        {
            await RuntimeScenario.Until(
                async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
                "the entry is accepted");
        }
    }

    private static async Task PushNextCandle(IHost worker, RuntimeScenario scenario, Bot bot)
    {
        await RuntimeScenario.Until(() => Task.FromResult(scenario.Candles.Subscribers > 0), "the bots subscribe to live candles");
        Candle last = (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle;
        Candle next = FlatHistorySource.Candle(last.OpenTime.AddHours(1));
        scenario.Candles.Push(next);
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))!.Engine.LastClosedCandle == next, "the live candle is committed");
    }

    private static async Task<StrategyVersionId> Save(IHost worker, Bot bot, Func<StrategyDefinitionInternal, StrategyDefinitionInternal> change)
    {
        using IServiceScope scope = worker.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        StrategyInternal current = await sender.Send(new GetStrategyQuery(bot.OwnerId.Value, bot.StrategyId.Value));
        StrategyInternal saved = await sender.Send(
            new SaveStrategyCommand(
                bot.OwnerId.Value,
                bot.StrategyId.Value,
                current.CurrentVersion,
                current.Name,
                change(current.Definition),
                current.Layout,
                ConfirmLiveImpact: true));
        StrategyVersion version = (await scope.ServiceProvider.GetRequiredService<IStrategyRepository>()
            .GetVersion(bot.StrategyId, new StrategyVersionNumber(saved.CurrentVersion), CancellationToken.None))!;

        return version.Id;
    }

    private static async Task<StrategyVersionId?> Versions(IHost worker, IReadOnlyList<Bot> bots)
    {
        StrategyVersionId[] versions = [.. await Task.WhenAll(bots.Select(async bot => (await RuntimeScenario.Read(worker, bot.Id)).StrategyVersionId))];

        return versions.Distinct().Count() == 1 ? versions[0] : null;
    }

    /// <summary>
    ///     The same definition with one block under another identifier, so the engine sees the old block removed.
    /// </summary>
    private static StrategyDefinitionInternal Renamed(StrategyDefinitionInternal definition, string blockId, string newId)
    {
        string json = JsonSerializer.Serialize(definition).Replace($"\"{blockId}\"", $"\"{newId}\"", StringComparison.Ordinal);

        return JsonSerializer.Deserialize<StrategyDefinitionInternal>(json)!;
    }
}