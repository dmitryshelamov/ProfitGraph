using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Positions;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     Short scanner trades (ADR 0061) over HTTP on the test venue made a margin venue: the leverage in the settings, short cards
///     beside the long ones, a created short trade's bot running the owner's short strategy at that leverage, and a buy at the target
///     closing it, and a short beyond the four hours' peak (ADR 0072). Values are invented; no venue is reached.
/// </summary>
[NonParallelizable]
public sealed class ScanShortTradeTests
{
    private const string Venue = TestHistoryVenue.WithHistory;
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        Dictionary<string, string> settings = BotApi.Settings();
        settings["ScanTrades:Timeframe"] = "1h";

        // The test venue offers hours, four hours (for the extremes' cards) and days; levels come from hours and days.
        settings["ScanTrades:LevelTimeframes:0"] = "1h";
        settings["ScanTrades:LevelTimeframes:1"] = "1d";
        _api = new ProfitGraphApiFactory(
            settings,
            services: services =>
            {
                services.AddSingleton<IMarketScanSource, TestScanSource>();
                services.RemoveAll<IVenueBotSettings>();
                services.AddSingleton<FakeLeverageSettings>();
                services.AddSingleton<IVenueBotSettings>(provider => provider.GetRequiredService<FakeLeverageSettings>());
                services.AddSingleton<IVenueLeverage>(provider => provider.GetRequiredService<FakeLeverageSettings>());

                foreach (ServiceDescriptor venue in services.Where(item => item.ImplementationInstance is TestHistoryVenue { Id.Value: Venue }).ToArray())
                {
                    services.Remove(venue);
                }

                services.AddSingleton<IVenueDescriptor>(
                    new TestHistoryVenue(Venue) { SupportsShort = true, Timeframes = [Timeframe.Parse("1h"), Timeframe.Parse("4h"), Timeframe.Parse("1d")] });
            });
        _api.Services.GetRequiredService<FakeHistorySource>().Candles = ScanLevelsTests.Shaped;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        // The database is shared by the run's fixtures; tests that read every venue's snapshot expect none of this venue.
        using (IServiceScope scope = _api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(Venue, DateTimeOffset.UtcNow, [])));
        }

        await _api.DisposeAsync();
    }

    [Test]
    public async Task AShortCardCreatesABotOfTheShortStrategyAtTheLeverageAndFollowsIt()
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        HttpResponseMessage saved = await owner.Client.PutAsJsonAsync(
            $"/api/scanner/{Venue}/trade-settings",
            new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000", leverage = "4" });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await saved.Content.ReadFromJsonAsync<ScanTradeSettingsResponse>(WebJson))!.Leverage.ShouldBe("4");
        await Scan("SHORTAUSDT");

        var ideas = (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol=SHORTAUSDT", WebJson))!;

        // Hours: support 90, resistance 110 and an average range of 1.375; a short sells at 100 with its stop a quarter range above 110.
        ScanTradeIdeaResponse levels = ideas.Ideas.Single(idea => idea is { Direction: "Short", Timeframe: "1h", Scenario: "Levels" });
        (levels.Entry, levels.Stop, levels.Target).ShouldBe(("100", "110.34375", "90"));
        ideas.Ideas.ShouldContain(idea => idea.Direction == "Long");

        HttpResponseMessage created = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new
            {
                symbol = "SHORTAUSDT",
                timeframe = levels.Timeframe,
                scenario = levels.Scenario,
                entry = levels.Entry,
                stop = levels.Stop,
                target = levels.Target,
                direction = "Short"
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var trade = (await created.Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        var bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");

        // A 10.34375 % stop, a limit entry at 0.08 % and a market stop at 0.1 % lose 10.52375 % of the amount: 5 / 0.1052375 = 47.51.
        (trade.Direction, trade.Amount).ShouldBe(("Short", "47.51"));
        bot.GetProperty("name").GetString().ShouldBe("SHORTAUSDT · шорт · уровни 1h");
        bot.GetProperty("parameters").GetProperty("sellAmount").GetString().ShouldBe("47.51");
        bot.GetProperty("allocationCap").GetString().ShouldBe("11.96");
        bot.GetProperty("maxOrderQuote").GetString().ShouldBe("47.51");

        using (IServiceScope scope = _api.Services.CreateScope())
        {
            var strategies = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
            StrategyVersion version = (await strategies.GetVersions(new StrategyId(bot.GetProperty("strategyId").GetGuid()), CancellationToken.None)).Single();
            (version.Definition.Direction, version.Definition.TemplateKind).ShouldBe(
                (PositionDirection.Short, (StrategyTemplateKind?)StrategyTemplateKind.LevelTradeShort));
        }

        var botId = new BotId(trade.BotId);
        await Publish(new BotPositionOpened(botId, BotPositionCycleId.New(), DateTimeOffset.UtcNow));
        await Publish(new BotOrderFilled(botId, Cover(LevelTradeStrategyBlockFactory.TargetTag), DateTimeOffset.UtcNow));
        await Publish(new BotPositionClosed(botId, BotPositionCycleId.New(), DateTimeOffset.UtcNow, RealizedPnl: 4.8m));
        ScanLiveTradeResponse closed = (await owner.Client.GetFromJsonAsync<List<ScanLiveTradeResponse>>($"/api/scanner/{Venue}/trades?symbol=SHORTAUSDT", WebJson))!
            .Single();

        (closed.Status, closed.RealizedPnl, closed.Direction).ShouldBe(("TookProfit", "4.8", "Short"));
    }

    [Test]
    public async Task AShortBeyondTheFourHoursPeakStopsAboveItsHighestHigh()
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        (await owner.Client.PutAsJsonAsync($"/api/scanner/{Venue}/trade-settings", new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var history = _api.Services.GetRequiredService<FakeHistorySource>();

        // Two hours ago the pair peaked at 104; the hours' average range becomes 1.5 (36 / 24).
        history.Candles = (timeframe, from, to) =>
        [
            .. ScanLevelsTests.Shaped(timeframe, from, to)
                .Select(candle => timeframe.Key == "1h" && candle.OpenTime == timeframe.FloorToGrid(DateTimeOffset.UtcNow).AddHours(-2)
                    ? candle with { High = 104m }
                    : candle)
        ];

        try
        {
            await Scan("SHORTPUSDT");

            var ideas = (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol=SHORTPUSDT", WebJson))!;

            // A quarter range above the peak is 104.375; two risks of 4.375 below the price of 100 is 91.25. The low of the four hours is the
            // price, so no long is proposed beyond it.
            ScanTradeIdeaResponse peak = ideas.Ideas.Single(idea => idea.Scenario == "Extreme");
            (peak.Direction, peak.Timeframe, peak.Entry, peak.Stop, peak.Target).ShouldBe(("Short", "4h", "100", "104.375", "91.25"));

            HttpResponseMessage created = await owner.Client.PostAsJsonAsync(
                $"/api/scanner/{Venue}/trades",
                new
                {
                    symbol = "SHORTPUSDT", timeframe = peak.Timeframe, scenario = peak.Scenario, entry = peak.Entry, stop = peak.Stop, target = peak.Target,
                    direction = "Short"
                });
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var trade = (await created.Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
            var bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");

            bot.GetProperty("name").GetString().ShouldBe("SHORTPUSDT · шорт · стоп за пиком 4h");
        }
        finally
        {
            history.Candles = ScanLevelsTests.Shaped;
        }
    }

    [Test]
    public async Task AShortWithItsTargetAboveTheEntryIsRefused()
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        (await owner.Client.PutAsJsonAsync($"/api/scanner/{Venue}/trade-settings", new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await Scan("SHORTBUSDT");

        HttpResponseMessage refused = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "SHORTBUSDT", timeframe = "1h", scenario = "Levels", entry = "100", stop = "90", target = "110", direction = "Short" });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static BotOrderFill Cover(string tag)
    {
        return new BotOrderFill(
            engineOrderId: 2,
            OrderSide.Buy,
            tag,
            grossBase: 0.49m,
            grossQuote: 44.1m,
            fee: 0.04m,
            FeeAsset.Quote,
            realizedPnl: null,
            quantityStep: 0.01m,
            priceTick: 0.01m,
            quotePrecision: 2);
    }

    private async Task Scan(string symbol)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        var row = new ScanSnapshotRowInternal(symbol, new ScanMetrics { Price = 100m }, Incomplete: false);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(Venue, DateTimeOffset.UtcNow, [row])));
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
}