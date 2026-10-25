using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotWaitingOrders;
using ProfitGraph.Application.Common.Operations;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Venues.Steam;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     A Steam Ladder bot end to end against the mock Steam (ADR 0022, wave 3 live milestone): buys confirmed on the phone and
///     proven from market history, items under their trade hold until they unlock, listings confirmed and sold, with exact
///     positions and Steam's seller fees; and the bot through a restart during a confirmation, an expired session, a
///     background 429, an item returned under a new ID and a CAPTCHA.
/// </summary>
[NonParallelizable]
public sealed class SteamLadderTests
{
    private const string HistoryPath = "/market/myhistory/render/";
    private const string ApprovePath = "/mobileconf/ajaxop";
    private const string CreatePath = "/market/createbuyorder/";

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
        (SteamLadderScenario scenario, IHost worker, Bot bot) = await RunningLadder();

        using (worker)
        {
            // The start buys at the lowest listing; the buy order exists only once its mobile confirmation is approved.
            await Level(scenario, worker, bot, price: 48000, items: 1);
            await scenario.NextCandle(worker, bot);
            await Level(scenario, worker, bot, price: 43200, items: 2);
            await scenario.NextCandle(worker, bot);
            await Level(scenario, worker, bot, price: 38880, items: 3);
            await scenario.NextCandle(worker, bot);

            // The exit for all three items waits for their trade hold: nothing is listed yet.
            await RuntimeScenario.Until(
                async () => (await Waiting(worker, bot)).Any(order => order.Code == OrderWaitingCode.SettlementHold),
                "the exit waits for the hold");
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
    }

    [Test]
    public async Task ARestartWhileABuyWaitsForItsConfirmationApprovesAndPlacesItOnce()
    {
        (SteamLadderScenario scenario, Guid owner, Guid credential) = await NewAccount();
        Bot bot;

        // The phone shows no confirmation yet when the process stops.
        scenario.Steam.Override = request => request.RequestUri!.AbsolutePath == "/mobileconf/getlist" ? Answer("""{"success":true,"conf":[]}""") : null;

        using (IHost first = scenario.Worker(credential))
        {
            await first.StartAsync();
            await SteamLadderScenario.UntilReady(first, credential);
            bot = await SteamLadderScenario.RunningLadder(first, owner, credential);
            await RuntimeScenario.Until(async () => await JournalState(bot) == "AwaitingConfirmation", "the buy waits for its confirmation");
            await first.StopAsync();
        }

        scenario.Steam.Override = null;
        using IHost second = scenario.Worker(credential);
        await second.StartAsync();
        await Level(scenario, second, bot, price: 48000, items: 1);

        scenario.Steam.Count(ApprovePath).ShouldBe(1);
        scenario.Steam.Count(CreatePath).ShouldBe(2);
        await second.StopAsync();
    }

    [Test]
    public async Task AnExpiredSessionSignsInAgainAndTheBotGoesOn()
    {
        (SteamLadderScenario scenario, IHost worker, Bot bot) = await RunningLadder();

        using (worker)
        {
            await RuntimeScenario.Until(() => Task.FromResult(scenario.Steam.BuyOrders() is [_]), "the start's buy order is placed");
            int signIns = scenario.Steam.Count("/jwt/finalizelogin");

            scenario.Steam.ExpireSession();
            await RuntimeScenario.Until(() => Task.FromResult(scenario.Steam.Count("/jwt/finalizelogin") > signIns), "a background read signs in again");
            await Level(scenario, worker, bot, price: 48000, items: 1);

            scenario.Steam.Count(CreatePath).ShouldBe(2);
            await worker.StopAsync();
        }
    }

    [Test]
    public async Task ARateLimitedBackgroundReadCoolsTheAccountDownAndNoWriteGoesTwice()
    {
        (SteamLadderScenario scenario, IHost worker, Bot bot) = await RunningLadder();

        using (worker)
        {
            await RuntimeScenario.Until(() => Task.FromResult(scenario.Steam.BuyOrders() is [_]), "the start's buy order is placed");
            var limited = 0;

            scenario.Steam.Override = request =>
                request.RequestUri!.AbsolutePath == HistoryPath && Interlocked.Increment(ref limited) == 1 ? TooManyRequests() : null;
            await RuntimeScenario.Until(() => Task.FromResult(Volatile.Read(ref limited) > 1), "the history is read again after the cooldown");
            await Level(scenario, worker, bot, price: 48000, items: 1);

            scenario.Steam.Count(CreatePath).ShouldBe(2);
            scenario.Steam.Count(ApprovePath).ShouldBe(1);
            await worker.StopAsync();
        }
    }

    [Test]
    public async Task ACaptchaHoldsTheAccountsBotsUntilTheOwnerResumes()
    {
        (SteamLadderScenario scenario, IHost worker, Bot bot) = await RunningLadder();

        using (worker)
        {
            await RuntimeScenario.Until(() => Task.FromResult(scenario.Steam.BuyOrders() is [_]), "the start's buy order is placed");
            var asked = 0;

            scenario.Steam.Override = request => request.RequestUri!.AbsolutePath == HistoryPath && Interlocked.Increment(ref asked) == 1
                ? Answer("""{"success":false,"captcha_needed":true}""")
                : null;
            await RuntimeScenario.Until(
                async () => await RuntimeScenario.Read(worker, bot.Id) is
                    { RuntimeStatus: BotRuntimeStatus.Degraded, StatusReason: BotStatusReason.VenueAccountProtection },
                "the CAPTCHA stops the bot");
            scenario.Steam.SellToBuyOrders().ShouldBe(1);
            int reads = scenario.Steam.Count(HistoryPath);
            await Task.Delay(TimeSpan.FromSeconds(3));
            scenario.Steam.Count(HistoryPath).ShouldBe(reads, "The open protection sends nothing.");

            await Resume(worker, bot.CredentialId.Value);
            await RuntimeScenario.Until(async () => (await SteamLadderScenario.Position(worker, bot)).BaseTotal == 1m, "the bot runs again and hears its fill");

            (await RuntimeScenario.Read(worker, bot.Id)).StatusReason.ShouldBeNull();
            scenario.Steam.Count(CreatePath).ShouldBe(2);
            await worker.StopAsync();
        }
    }

    [Test]
    public async Task AnItemReturnedFromItsListingUnderANewIdIsSoldUnderIt()
    {
        (SteamLadderScenario scenario, IHost worker, Bot bot) = await RunningLadder();

        using (worker)
        {
            await Level(scenario, worker, bot, price: 48000, items: 1);
            await scenario.NextCandle(worker, bot);
            scenario.Clock.Advance(scenario.Steam.TradeHold + TimeSpan.FromMinutes(1));
            await RuntimeScenario.Until(() => Task.FromResult(scenario.Steam.Listings() is [{ Confirmed: true }]), "the first item is listed");
            string first = scenario.Steam.Listings()[0].AssetId;

            // The next level replaces the exit: its listing is removed and the item comes back under a new ID.
            scenario.Steam.ReturnsUnderNewIds = true;
            await Level(scenario, worker, bot, price: 43200, items: 2);
            scenario.Clock.Advance(scenario.Steam.TradeHold + TimeSpan.FromMinutes(1));
            await UntilWithCandles(
                scenario,
                worker,
                bot,
                () => scenario.Steam.Listings() is { Count: 2 } listings && listings.All(listing => listing.Confirmed),
                "both items are listed again");
            scenario.Steam.BuyListings().ShouldBe(2);
            await RuntimeScenario.Until(async () => (await SteamLadderScenario.Position(worker, bot)).BaseTotal == 0m, "the sales close the position");

            scenario.Steam.Sold().Count.ShouldBe(2);
            scenario.Steam.Sold().ShouldNotContain(first);
            await worker.StopAsync();
        }
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

    /// <summary>
    ///     Closes an hour every two seconds until the condition holds, so the bot decides again with what it learned meanwhile.
    /// </summary>
    private static async Task UntilWithCandles(SteamLadderScenario scenario, IHost worker, Bot bot, Func<bool> condition, string what)
    {
        DateTime next = DateTime.UtcNow;
        await RuntimeScenario.Until(
            async () =>
            {
                if (DateTime.UtcNow >= next)
                {
                    await scenario.NextCandle(worker, bot);
                    next = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                }

                return condition();
            },
            what);
    }

    private static string NewSteamId()
    {
        return $"7656119{Random.Shared.NextInt64(minValue: 1_000_000_000, maxValue: 9_999_999_999)}";
    }

    private static HttpResponseMessage Answer(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage TooManyRequests()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));

        return response;
    }

    private static async Task<IReadOnlyList<BotOrderWaitingInternal>> Waiting(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotWaitingOrdersQuery(bot.OwnerId.Value, bot.Id.Value));
    }

    private static async Task<string?> JournalState(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<string?>("SELECT state FROM steam_order_journal WHERE bot_id = @id", new { id = bot.Id.Value });
    }

    private static async Task Resume(IHost host, Guid credential)
    {
        await host.Services.GetServices<IVenueAccountProtection>()
            .Single(protection => protection.Venue == SteamVenue.VenueId)
            .Resume(credential, CancellationToken.None);
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

    private async Task<(SteamLadderScenario Scenario, Guid Owner, Guid Credential)> NewAccount()
    {
        UserInternal owner = await _api.CreateAccount(UserRole.User);
        string steamId = NewSteamId();
        Guid credential = await Import(owner.Login, steamId);

        return (new SteamLadderScenario(steamId), owner.Id, credential);
    }

    /// <summary>
    ///     A started Worker with a new account ready to trade and a Ladder bot on it; the test stops and disposes the Worker.
    /// </summary>
    private async Task<(SteamLadderScenario Scenario, IHost Worker, Bot Bot)> RunningLadder()
    {
        (SteamLadderScenario scenario, Guid owner, Guid credential) = await NewAccount();
        IHost worker = scenario.Worker(credential);
        await worker.StartAsync();
        await SteamLadderScenario.UntilReady(worker, credential);

        return (scenario, worker, await SteamLadderScenario.RunningLadder(worker, owner, credential));
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