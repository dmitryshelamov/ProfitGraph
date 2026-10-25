using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotWaitingOrders;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     A Steam Ladder bot end to end against the mock Steam (ADR 0022, wave 3 live milestone): buys confirmed on the phone and
///     proven from market history, items under their trade hold until they unlock, listings confirmed and sold, with exact
///     positions and Steam's seller fees.
/// </summary>
[NonParallelizable]
public sealed class SteamLadderTests
{
    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _api = new ProfitGraphApiFactory(BotApi.Settings());
        _ = _api.Services;
    }

    [SetUp]
    public Task Isolate()
    {
        return RuntimeScenario.StopEveryBot();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task ALadderBuysThreeLevelsWaitsOutTheHoldAndSellsWithSteamsFees()
    {
        UserInternal owner = await _api.CreateAccount(UserRole.User);
        string steamId = NewSteamId();
        Guid credential = await Import(owner.Login, steamId);
        var scenario = new SteamLadderScenario(steamId);
        using IHost worker = scenario.Worker(credential);
        await worker.StartAsync();
        await SteamLadderScenario.UntilReady(worker, credential);
        Bot bot = await SteamLadderScenario.RunningLadder(worker, owner.Id, credential);

        // The start buys at the lowest listing; the buy order exists only once its mobile confirmation is approved.
        await Level(scenario, worker, bot, price: 48000, items: 1);
        await scenario.NextCandle(worker, bot);
        await Level(scenario, worker, bot, price: 43200, items: 2);
        await scenario.NextCandle(worker, bot);
        await Level(scenario, worker, bot, price: 38880, items: 3);
        await scenario.NextCandle(worker, bot);

        // The exit for all three items waits for their trade hold: nothing is listed yet.
        await RuntimeScenario.Until(async () => (await Waiting(worker, bot)).Any(order => order.Code == OrderWaitingCode.SettlementHold), "the exit waits for the hold");
        scenario.Steam.Listings().ShouldBeEmpty();
        scenario.Steam.BuyOrders().ShouldBeEmpty();

        scenario.Clock.Advance(scenario.Steam.TradeHold + TimeSpan.FromMinutes(1));
        await RuntimeScenario.Until(
            () => Task.FromResult(scenario.Steam.Listings() is { Count: 3 } listings && listings.All(listing => listing.Confirmed)),
            "the unlocked items are listed and confirmed");
        long received = scenario.Steam.Listings().Sum(listing => listing.SellerPrice);
        scenario.Steam.BuyListings().ShouldBe(3);
        await RuntimeScenario.Until(async () => (await SteamLadderScenario.Position(worker, bot)).BaseTotal == 0m, "the sales close the position");

        BotPositionInternal position = await SteamLadderScenario.Position(worker, bot);
        position.RealizedPnl.ShouldBe(received / 100m - (480m + 432m + 388.80m));
        (await Fills(bot)).ShouldBe(
        [
            ("Buy", 1m, 480m, 0m),
            ("Buy", 1m, 432m, 0m),
            ("Buy", 1m, 388.80m, 0m),
            ("Sell", 3m, await SoldFor(bot), await SoldFor(bot) - received / 100m)
        ]);

        // The closed position restarts the ladder, whose new entry may wait for its confirmation; no item waits for its hold.
        (await Waiting(worker, bot)).ShouldAllBe(order => order.Code != OrderWaitingCode.SettlementHold);
        await worker.StopAsync();
    }

    /// <summary>
    ///     Waits for the buy order of the next level, lets sellers fill it and waits until the bot holds that many items.
    /// </summary>
    private static async Task Level(SteamLadderScenario scenario, IHost worker, Bot bot, long price, int items)
    {
        await RuntimeScenario.Until(
            () => Task.FromResult(scenario.Steam.BuyOrders() is [{ Remaining: 1 } order] && order.Price == price),
            $"the buy order at {price} is confirmed and placed");
        scenario.Steam.SellToBuyOrders().ShouldBe(1);
        await RuntimeScenario.Until(async () => (await SteamLadderScenario.Position(worker, bot)).BaseTotal == items, $"the bot holds {items} items");
    }

    private static string NewSteamId()
    {
        return $"7656119{Random.Shared.NextInt64(minValue: 1_000_000_000, maxValue: 9_999_999_999)}";
    }

    private static async Task<IReadOnlyList<BotOrderWaitingInternal>> Waiting(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotWaitingOrdersQuery(bot.OwnerId.Value, bot.Id.Value));
    }

    /// <summary>
    ///     The bot's fills in order, the sells of one order summed: side, items, what the buyers paid and the fee.
    /// </summary>
    private static async Task<IReadOnlyList<(string Side, decimal Items, decimal Quote, decimal Fee)>> Fills(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return
        [
            .. await connection.QueryAsync<(string, decimal, decimal, decimal)>(
                sql: """
                     SELECT side, sum(gross_base), sum(abs(gross_quote)), sum(fee)
                     FROM bot_fills WHERE bot_id = @id GROUP BY engine_order_id, side ORDER BY min(sequence)
                     """,
                new { id = bot.Id.Value })
        ];
    }

    private static async Task<decimal> SoldFor(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT coalesce(sum(abs(gross_quote)), 0) FROM bot_fills WHERE bot_id = @id AND side = 'Sell'",
            new { id = bot.Id.Value });
    }

    private async Task<Guid> Import(string owner, string steamId)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        IOperatorCommand command = scope.ServiceProvider.GetServices<IOperatorCommand>().Single(candidate => candidate.Name == "steam-import");
        await using var output = new StringWriter();
        var input = $$"""
                      { "owner": "{{owner}}", "label": "ladder",
                        "authenticator": { "shared_secret": "c2hhcmVk", "identity_secret": "{{MockSteamMarket.IdentitySecret}}", "device_id": "android:0",
                          "Session": { "SteamID": {{steamId}}, "RefreshToken": "{{MockSteamMarket.RefreshToken}}" } } }
                      """;

        (await command.Run(new StringReader(input), output, CancellationToken.None)).ShouldBe(expected: 0, output.ToString());

        return Guid.Parse(output.ToString().Split("credential ")[1].Split(';')[0]);
    }
}