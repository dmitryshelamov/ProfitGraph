using System.Globalization;
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
///     Scanner trade cards over HTTP (ADR 0060) on the test venue: settings with the owner's credential, cards on the levels of every
///     timeframe the venue offers (hours and days, shaped as in <see cref="ScanLevelsTests" />), created trades with their own started
///     bots of the level trade strategy, the trade following the bot's events, and the Worker's
///     tracking that ends a trade whose bot the owner stopped and stops the bot of a finished trade. Values are invented; no venue is
///     contacted.
/// </summary>
[NonParallelizable]
public sealed class ScanTradeTests
{
    private const string Venue = TestHistoryVenue.WithHistory;
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _api = null!;
    private FakeHistorySource _history = null!;

    [OneTimeSetUp]
    public void Start()
    {
        // The test venue has no minutes, so trades run on hours here.
        Dictionary<string, string> settings = BotApi.Settings();
        settings["ScanTrades:Timeframe"] = "1h";
        _api = new ProfitGraphApiFactory(settings, services: services => services.AddSingleton<IMarketScanSource, TestScanSource>());
        _history = _api.Services.GetRequiredService<FakeHistorySource>();
    }

    [SetUp]
    public void ShapeVenue()
    {
        _history.Reset();
        _history.Candles = ScanLevelsTests.Shaped;
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

        ScanTradeIdeasResponse ideas = await Ideas(owner, "TRADEAUSDT");

        // Hours: support 90, resistance 110; days: support 95, resistance 120; the last hour did not rise, so no pullback.
        (ideas.Price, ideas.HasSettings).ShouldBe(("100", true));
        ideas.Ideas.Select(idea => (idea.Timeframe, idea.Scenario, idea.Stop, idea.Target, idea.Taken))
            .ShouldBe(
            [
                ("1h", "Levels", "90", "110", false), ("1h", "Target2R", "90", "120", false), ("1d", "Levels", "95", "120", false),
                ("1d", "Target2R", "95", "110", false)
            ]);
        ScanTradeIdeaResponse levels = ideas.Ideas[2];
        (levels.Entry, levels.RewardRisk, levels.EntryValidityMinutes).ShouldBe(("100", "4", 60));

        // The test venue's rates for the owner's credential; the snapshot row has no spread.
        ideas.Costs.ShouldBe(new ScanTradeCostsResponse("0.0008", "0.001", "0", FeesFromVenue: true, SpreadKnown: false));
        ideas.TightStops.ShouldBe(0);

        // 96.525 reach 120 from 100 and pay a limit fee of 0.08 % twice: 96.525 × 0.1984.
        decimal.Parse(levels.Profit!, CultureInfo.InvariantCulture).ShouldBe(expected: 19.1506m, tolerance: 0.0001m);

        HttpResponseMessage created = await Create(owner, "TRADEAUSDT", levels);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var trade = (await created.Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        var bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");

        // A 5 % stop, a limit entry at 0.08 % and a market stop at 0.1 % lose 5.18 % of the amount: 5 / 0.0518 = 96.52 after rounding down
        // to cents.
        (trade.Status, trade.Amount, trade.Scenario).ShouldBe(("Waiting", "96.52", nameof(ScanScenario.Levels)));
        bot.GetProperty("name").GetString().ShouldBe("TRADEAUSDT · уровни 1d");
        bot.GetProperty("allowForeignOrders").GetBoolean().ShouldBeTrue();
        bot.GetProperty("desiredState").GetString().ShouldBe("Running");
        bot.GetProperty("timeframe").GetString().ShouldBe("1h");
        bot.GetProperty("parameters").GetProperty("entryPrice").GetString().ShouldBe("100");
        bot.GetProperty("allocationCap").GetString().ShouldBe("96.52");

        HttpResponseMessage twice = await Create(owner, "TRADEAUSDT", levels);
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(twice)).ShouldBe("scan_trade_live");
        (await Ideas(owner, "TRADEAUSDT")).Ideas.Select(idea => idea.Taken).ShouldBe([false, false, true, false]);
        HttpResponseMessage another = await Create(owner, "TRADEAUSDT", ideas.Ideas[1]);
        another.StatusCode.ShouldBe(HttpStatusCode.Created, await another.Content.ReadAsStringAsync());

        var botId = new BotId(trade.BotId);
        await Publish(new BotPositionOpened(botId, BotPositionCycleId.New(), DateTimeOffset.UtcNow));
        await Publish(new BotOrderFilled(botId, Sale(LevelTradeStrategyBlockFactory.TargetTag), DateTimeOffset.UtcNow));
        await Publish(new BotPositionClosed(botId, BotPositionCycleId.New(), DateTimeOffset.UtcNow, RealizedPnl: 11.4m));
        ScanLiveTradeResponse closed = (await Trades(owner, "TRADEAUSDT")).Single(item => item.Id == trade.Id);

        (closed.Status, closed.RealizedPnl).ShouldBe(("TookProfit", "11.4"));

        (await Track(trade.Id)).ShouldBeTrue();
        bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");
        bot.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        (await Track(trade.Id)).ShouldBeFalse();
    }

    [Test]
    public async Task IdeasWhoseStopIsNearerThanFeesAndSpreadAreLeftOutAndRefused()
    {
        // A 5 % spread and 0.18 % of fees exceed the 5 % stops of days, but not the 10 % stops of hours.
        BotOwner owner = await SetUp("TRADEDUSDT", spread: 5m);

        ScanTradeIdeasResponse ideas = await Ideas(owner, "TRADEDUSDT");
        HttpResponseMessage tight = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "TRADEDUSDT", timeframe = "1d", scenario = "Levels", entry = "100", stop = "95", target = "120" });

        ideas.Ideas.Select(idea => (idea.Timeframe, idea.Scenario)).ShouldBe([("1h", "Levels"), ("1h", "Target2R")]);
        (ideas.TightStops, ideas.Costs.Spread, ideas.Costs.SpreadKnown).ShouldBe((2, "0.05", true));
        tight.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(tight)).ShouldBe("scan_trade_stop_too_tight");
    }

    [Test]
    public async Task AnEntryFarFromThePriceAndAPairTheScannerLacksAreRefused()
    {
        BotOwner owner = await SetUp("TRADEBUSDT");

        HttpResponseMessage moved = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "TRADEBUSDT", timeframe = "1h", scenario = "Pullback", entry = "90", stop = "85", target = "100" });
        HttpResponseMessage missing = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "TRADEZUSDT", timeframe = "1h", scenario = "Levels", entry = "100", stop = "95", target = "112" });

        moved.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(moved)).ShouldBe("scan_trade_entry_moved");
        missing.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(missing)).ShouldBe("scan_trade_pair_not_scanned");
    }

    [Test]
    public async Task AWaitingTradeWhoseBotTheOwnerStoppedIsCancelled()
    {
        BotOwner owner = await SetUp("TRADECUSDT");
        ScanTradeIdeasResponse ideas = await Ideas(owner, "TRADECUSDT");
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
        (await Ideas(owner, "TRADECUSDT")).Ideas.ShouldAllBe(idea => !idea.Taken);
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
                timeframe = idea.Timeframe,
                scenario = idea.Scenario,
                entry = idea.Entry,
                stop = idea.Stop,
                target = idea.Target
            });
    }

    private static async Task<ScanTradeIdeasResponse> Ideas(BotOwner owner, string symbol)
    {
        return (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol={symbol}", WebJson))!;
    }

    private static async Task<IReadOnlyList<ScanLiveTradeResponse>> Trades(BotOwner owner, string symbol)
    {
        return (await owner.Client.GetFromJsonAsync<List<ScanLiveTradeResponse>>($"/api/scanner/{Venue}/trades?symbol={symbol}", WebJson))!;
    }

    private async Task<BotOwner> SetUp(string symbol, decimal? spread = null)
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        (await owner.Client.PutAsJsonAsync($"/api/scanner/{Venue}/trade-settings", new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using IServiceScope scope = _api.Services.CreateScope();
        var row = new ScanSnapshotRowInternal(symbol, new ScanMetrics { Price = 100m, Spread = spread }, Incomplete: false);
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