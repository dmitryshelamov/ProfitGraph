using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Application.Scanner.TrackScanTrade;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Positions;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     Scanner trade cards over HTTP (ADR 0060) on the test venue: settings with the owner's credential, cards from the snapshot, a
///     created trade with its own started bot of the level trade strategy, the trade following the bot's events, and the Worker's
///     tracking that ends a trade whose bot the owner stopped and stops the bot of a finished trade. Values are invented; no venue is
///     contacted.
/// </summary>
[NonParallelizable]
public sealed class ScanTradeTests
{
    private const string Venue = TestHistoryVenue.WithHistory;
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        // The test venue has no minutes, so trades run on hours here.
        Dictionary<string, string> settings = BotApi.Settings();
        settings["ScanTrades:Timeframe"] = "1h";
        _api = new ProfitGraphApiFactory(settings, services: services => services.AddSingleton<IMarketScanSource, TestScanSource>());
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task SettingsTakeOnlyTheOwnersCredentialOfTheVenue()
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        BotOwner other = await BotApi.NewOwner(_api, TestHistoryVenue.WithoutHistory);

        (await owner.Client.GetFromJsonAsync<ScanTradeSettingsStateResponse>($"/api/scanner/{Venue}/trade-settings", WebJson))!.Settings.ShouldBeNull();
        HttpResponseMessage foreign = await owner.Client.PutAsJsonAsync(
            $"/api/scanner/{Venue}/trade-settings",
            new { credentialId = other.CredentialId, risk = "5", maxAmount = "100" });
        HttpResponseMessage saved = await owner.Client.PutAsJsonAsync(
            $"/api/scanner/{Venue}/trade-settings",
            new { credentialId = owner.CredentialId, risk = "5", maxAmount = "100" });

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        ScanTradeSettingsResponse settings = (await owner.Client.GetFromJsonAsync<ScanTradeSettingsStateResponse>($"/api/scanner/{Venue}/trade-settings", WebJson))!
            .Settings!;
        (settings.CredentialId, settings.Risk, settings.MaxAmount).ShouldBe((owner.CredentialId, "5", "100"));
    }

    [Test]
    public async Task ACardCreatesAStartedBotOfTheLevelTradeAndFollowsIt()
    {
        BotOwner owner = await SetUp("TRADEAUSDT");

        var ideas = (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol=TRADEAUSDT", WebJson))!;
        ScanTradeIdeaResponse levels = ideas.Ideas.Single(idea => idea.Scenario == nameof(ScanScenario.Levels));

        (ideas.Price, ideas.HasSettings, ideas.HasLiveTrade).ShouldBe(("100", true, false));
        (levels.Entry, levels.Stop, levels.Target, levels.RewardRisk, levels.EntryValidityMinutes).ShouldBe(("100", "95", "112", "2.4", 60));
        levels.Amount.ShouldNotBeNull();

        HttpResponseMessage created = await Create(owner, "TRADEAUSDT", levels);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var trade = (await created.Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        var bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");

        // A 5 % stop and 0.1 % on each side lose 5.2 % of the amount: 5 / 0.052 = 96.15 after rounding down to cents.
        (trade.Status, trade.Amount, trade.Scenario).ShouldBe(("Waiting", "96.15", nameof(ScanScenario.Levels)));
        bot.GetProperty("name").GetString().ShouldBe("TRADEAUSDT · уровни");
        bot.GetProperty("desiredState").GetString().ShouldBe("Running");
        bot.GetProperty("timeframe").GetString().ShouldBe("1h");
        bot.GetProperty("parameters").GetProperty("entryPrice").GetString().ShouldBe("100");
        bot.GetProperty("allocationCap").GetString().ShouldBe("96.15");

        HttpResponseMessage twice = await Create(owner, "TRADEAUSDT", levels);
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(twice)).ShouldBe("scan_trade_live");

        var botId = new BotId(trade.BotId);
        await Publish(new BotPositionOpened(botId, BotPositionCycleId.New(), DateTimeOffset.UtcNow));
        await Publish(new BotOrderFilled(botId, Sale(LevelTradeStrategyBlockFactory.TargetTag), DateTimeOffset.UtcNow));
        await Publish(new BotPositionClosed(botId, BotPositionCycleId.New(), DateTimeOffset.UtcNow, RealizedPnl: 11.4m));
        ScanLiveTradeResponse closed = (await Trades(owner, "TRADEAUSDT")).Single();

        (closed.Status, closed.RealizedPnl).ShouldBe(("TookProfit", "11.4"));

        (await Track(trade.Id)).ShouldBeTrue();
        bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");
        bot.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        (await Track(trade.Id)).ShouldBeFalse();
    }

    [Test]
    public async Task AnEntryFarFromThePriceAndAPairTheScannerLacksAreRefused()
    {
        BotOwner owner = await SetUp("TRADEBUSDT");

        HttpResponseMessage moved = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "TRADEBUSDT", scenario = "Pullback", entry = "90", stop = "85", target = "100" });
        HttpResponseMessage missing = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "TRADEZUSDT", scenario = "Levels", entry = "100", stop = "95", target = "112" });

        moved.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(moved)).ShouldBe("scan_trade_entry_moved");
        missing.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(missing)).ShouldBe("scan_trade_pair_not_scanned");
    }

    [Test]
    public async Task AWaitingTradeWhoseBotTheOwnerStoppedIsCancelled()
    {
        BotOwner owner = await SetUp("TRADECUSDT");
        var ideas = (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol=TRADECUSDT", WebJson))!;
        var trade = (await (await Create(owner, "TRADECUSDT", ideas.Ideas[0])).Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        (await owner.Client.PostAsync($"{BotApi.Path}/{trade.BotId}/stop", content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Within a minute of its creation a stopped bot without a run may still be starting.
        (await Track(trade.Id)).ShouldBeFalse();

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE scan_trades SET created_at = created_at - interval '2 minutes' WHERE id = @Id", new { trade.Id });
        }

        (await Track(trade.Id)).ShouldBeTrue();
        ScanLiveTradeResponse cancelled = (await Trades(owner, "TRADECUSDT")).Single();

        cancelled.Status.ShouldBe("Cancelled");
        (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol=TRADECUSDT", WebJson))!.HasLiveTrade.ShouldBeFalse();
    }

    private static BotOrderFill Sale(string tag)
    {
        return new BotOrderFill(
            engineOrderId: 2,
            OrderSide.Sell,
            tag,
            grossBase: 0.9m,
            grossQuote: 100.8m,
            fee: 0.1m,
            FeeAsset.Quote,
            realizedPnl: 11.4m,
            quantityStep: 0.01m,
            priceTick: 0.01m,
            quotePrecision: 2);
    }

    private static Task<HttpResponseMessage> Create(BotOwner owner, string symbol, ScanTradeIdeaResponse idea)
    {
        return owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new
            {
                symbol,
                scenario = idea.Scenario,
                entry = idea.Entry,
                stop = idea.Stop,
                target = idea.Target
            });
    }

    private static async Task<IReadOnlyList<ScanLiveTradeResponse>> Trades(BotOwner owner, string symbol)
    {
        return (await owner.Client.GetFromJsonAsync<List<ScanLiveTradeResponse>>($"/api/scanner/{Venue}/trades?symbol={symbol}", WebJson))!;
    }

    private async Task<BotOwner> SetUp(string symbol)
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        (await owner.Client.PutAsJsonAsync($"/api/scanner/{Venue}/trade-settings", new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using IServiceScope scope = _api.Services.CreateScope();
        var row = new ScanSnapshotRowInternal(symbol, new ScanMetrics { Price = 100m, Support = 95m, Resistance = 112m }, Incomplete: false);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(Venue, DateTimeOffset.UtcNow, [row])));

        return owner;
    }

    private async Task Publish(INotification notification)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(notification);

                    return true;
                },
                CancellationToken.None);
    }

    private async Task<bool> Track(Guid tradeId)
    {
        using IServiceScope scope = _api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TrackScanTradeCommand(tradeId));
    }
}