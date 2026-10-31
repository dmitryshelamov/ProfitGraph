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
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Application.Scanner.TrackScanTrade;
using ProfitGraph.Application.Scanner.Trades;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.MarkPrices;
using ProfitGraph.Domain.Bots.Positions;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
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

        // The test venue offers hours and days only.
        settings["ScanTrades:LevelTimeframes:0"] = "1h";
        settings["ScanTrades:LevelTimeframes:1"] = "1d";
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

        // Hours: support 90, resistance 110, an average range of 1.375; days: support 95, resistance 120, a range of 1.7916…; each stop lies
        // a quarter range below its support (ADR 0070). The last hour did not rise, so no pullback.
        (ideas.Price, ideas.HasSettings).ShouldBe(("100", true));
        ideas.Ideas.Select(idea => (idea.Timeframe, idea.Scenario, idea.Stop, idea.Target, idea.Taken))
            .ShouldBe(
            [
                ("1h", "Levels", "89.65625", "110", false), ("1h", "Target2R", "89.65625", "120.6875", false),
                ("1d", "Levels", "94.552083", "120", false),
                ("1d", "Target2R", "94.552083", "110.895834", false)
            ]);
        ScanTradeIdeaResponse levels = ideas.Ideas[2];
        (levels.Entry, levels.EntryValidityMinutes).ShouldBe(("100", 60));
        decimal.Parse(levels.RewardRisk, CultureInfo.InvariantCulture).ShouldBe(20m / 5.4479m, tolerance: 0.0001m);

        // The test venue's rates for the owner's credential; the snapshot row has no spread.
        ideas.Costs.ShouldBe(new ScanTradeCostsResponse("0.0008", "0.001", "0", FeesFromVenue: true, SpreadKnown: false));
        ideas.TightStops.ShouldBe(0);

        // 88.843 reach 120 from 100 and pay a limit fee of 0.08 % twice: 88.843 × 0.1984.
        decimal.Parse(levels.Profit!, CultureInfo.InvariantCulture).ShouldBe(expected: 17.6264m, tolerance: 0.0001m);

        HttpResponseMessage created = await Create(owner, "TRADEAUSDT", levels);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var trade = (await created.Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        var bot = await owner.Client.GetFromJsonAsync<JsonElement>($"{BotApi.Path}/{trade.BotId}");

        // A 5.448 % stop, a limit entry at 0.08 % and a market stop at 0.1 % lose 5.628 % of the amount: 5 / 0.05628 = 88.84 after
        // rounding down to cents.
        (trade.Status, trade.Amount, trade.Scenario).ShouldBe(("Waiting", "88.84", nameof(ScanScenario.Levels)));
        bot.GetProperty("name").GetString().ShouldBe("TRADEAUSDT · уровни 1d");
        bot.GetProperty("allowForeignOrders").GetBoolean().ShouldBeTrue();
        bot.GetProperty("desiredState").GetString().ShouldBe("Running");
        bot.GetProperty("timeframe").GetString().ShouldBe("1h");
        bot.GetProperty("parameters").GetProperty("entryPrice").GetString().ShouldBe("100");
        bot.GetProperty("allocationCap").GetString().ShouldBe("88.84");

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
    public async Task CardsTakeTheirOddsFromTheOwnersBacktestsOfCardsLikeThemShrunkTowardsTheRandomWalk()
    {
        // A NATR of 4 % puts the 10.34 % stop of the hours' level card in the band beyond 1.5 and up to 3 NATRs.
        BotOwner owner = await SetUp("TRADEOUSDT", natr: 4m);
        BotOwner stranger = await SetUp("TRADEOUSDT", natr: 4m);
        ScanTradeIdeasResponse before = await Ideas(owner, "TRADEOUSDT");
        ScanTradeIdeaResponse levels = before.Ideas.First(idea => idea is { Timeframe: "1h", Scenario: "Levels" });
        var proposal = new ScanTradeIdea(
            ScanScenario.Levels,
            Number(levels.Entry),
            Number(levels.Stop),
            Number(levels.Target),
            Number(levels.RewardRisk),
            TimeSpan.FromMinutes(levels.EntryValidityMinutes));

        // 160 of 200 cards like it filled, 64 of them reached the target, the 96 others lost 1.1 R on average.
        var odds = new ScanCardOdds(
            "1h",
            ScanScenario.Levels,
            PositionDirection.Long,
            ScanCardBands.Chance(proposal.RewardRisk),
            ScanCardBands.Stop(proposal, natrPercent: 4m)!.Value,
            Cards: 200,
            Filled: 160,
            Targets: 64,
            Stops: 96,
            MeanR: -0.1m,
            MissedMeanR: -1.1m);
        Guid backtestId = await StoreBacktest(owner, odds);

        // A later backtest of another preset over the same period, in which every card like it missed the target.
        Guid otherId = await StoreBacktest(owner, odds with { Targets = 0, Stops = 160 }, "Другой");

        ScanTradeIdeasResponse after = await Ideas(owner, "TRADEOUSDT", "Шансы");
        ScanTradeIdeasResponse pooled = await Ideas(owner, "TRADEOUSDT");

        (before.OddsSources.Count, levels.Odds).ShouldBe((0, null));
        ScanCardOddsSourceResponse source = after.OddsSources.ShouldHaveSingleItem();
        (source.BacktestId, source.PresetName, Number(source.Weight)).ShouldBe((backtestId, "Шансы", 1m));
        pooled.OddsSources.Select(item => item.BacktestId).ShouldBe([otherId, backtestId]);

        // The 40 % measured is shrunk towards the random walk of the band (ADR 0073), and its cautious end lies below it.
        var model = new ScanCardOddsModel([new ScanWeightedOdds([odds], Weight: 1m)], priorTrades: 50m, minimumTrades: 100);
        decimal expected = model.Of("1h", proposal, natrPercent: 4m, marketTrend: null, ScanTradeCosts.Flat(0.001m))!.Chance;
        ScanCardChanceResponse chance = after.Ideas.First(idea => idea is { Timeframe: "1h", Scenario: "Levels" }).Odds!;
        Number(chance.Chance).ShouldBe(Math.Round(expected, decimals: 4));
        Number(chance.ChanceLow).ShouldBeLessThan(Number(chance.Chance));
        (Number(chance.FillShare), Number(chance.MissedR), chance.Trades).ShouldBe((0.8m, -1.1m, 160));
        decimal win = Number(after.Ideas.First(idea => idea is { Timeframe: "1h", Scenario: "Levels" }).Profit!) / 5m;
        Number(chance.ExpectedR).ShouldBe(0.8m * (Number(chance.Chance) * win + (1m - Number(chance.Chance)) * -1.1m), tolerance: 0.0005m);
        after.Ideas.Where(idea => idea is not { Timeframe: "1h", Scenario: "Levels" }).ShouldAllBe(idea => idea.Odds == null);

        // Without a preset every preset's backtests count, so the missed cards of the other lower the chance.
        Number(pooled.Ideas.First(idea => idea is { Timeframe: "1h", Scenario: "Levels" }).Odds!.Chance).ShouldBeLessThan(Number(chance.Chance));
        (await Ideas(stranger, "TRADEOUSDT")).OddsSources.ShouldBeEmpty();
    }

    [Test]
    public async Task IdeasWhoseStopIsNearerThanFeesAndSpreadAreLeftOutAndRefused()
    {
        // A 6 % spread and 0.18 % of fees exceed the 5.45 % stops of days, but not the 10.34 % stops of hours.
        BotOwner owner = await SetUp("TRADEDUSDT", spread: 6m);

        ScanTradeIdeasResponse ideas = await Ideas(owner, "TRADEDUSDT");
        HttpResponseMessage tight = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "TRADEDUSDT", timeframe = "1d", scenario = "Levels", entry = "100", stop = "95", target = "120" });

        ideas.Ideas.Select(idea => (idea.Timeframe, idea.Scenario)).ShouldBe([("1h", "Levels"), ("1h", "Target2R")]);
        (ideas.TightStops, ideas.Costs.Spread, ideas.Costs.SpreadKnown).ShouldBe((2, "0.06", true));
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
    public async Task AnEntryGoesOnlyWhileThePriceIsBetweenItsStopAndTarget()
    {
        BotOwner owner = await SetUp("TRADEEUSDT");
        ScanTradeIdeasResponse ideas = await Ideas(owner, "TRADEEUSDT");

        // Hours: stop 89.66, target 110; days: stop 94.55, target 120 (stops a margin beyond the supports, ADR 0070). The pair then falls
        // to 93, below the days' stop only.
        var hours = (await (await Create(owner, "TRADEEUSDT", ideas.Ideas[0])).Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        var days = (await (await Create(owner, "TRADEEUSDT", ideas.Ideas[2])).Content.ReadFromJsonAsync<ScanLiveTradeResponse>(WebJson))!;
        await Price("TRADEEUSDT", price: 93m);

        (await Refusal(hours.BotId, LevelTradeStrategyBlockFactory.EntryTag)).ShouldBeNull();
        (await Refusal(days.BotId, LevelTradeStrategyBlockFactory.TargetTag)).ShouldBeNull();
        (await Refusal(days.BotId, LevelTradeStrategyBlockFactory.EntryTag)).ShouldBe("The price 93 is no longer between the trade's stop 94.552083 and target 120.");

        IReadOnlyList<ScanLiveTradeResponse> trades = await Trades(owner, "TRADEEUSDT");
        trades.Single(trade => trade.Id == hours.Id).Status.ShouldBe("Waiting");
        ScanLiveTradeResponse failed = trades.Single(trade => trade.Id == days.Id);
        (failed.Status, failed.FailureCode).ShouldBe(("Failed", "scan_trade_entry_beyond_levels"));

        // The bots here have no checkpoint, so a display price four minutes old leaves the hours' entry without a price.
        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE live_mark_prices SET observed_at = now() - interval '4 minutes' WHERE symbol = 'TRADEEUSDT'");
        }

        (await Refusal(hours.BotId, LevelTradeStrategyBlockFactory.EntryTag)).ShouldBe("The pair's current price is unknown.");
        (await Trades(owner, "TRADEEUSDT")).Single(trade => trade.Id == hours.Id).FailureCode.ShouldBe("scan_trade_entry_price_unknown");
        (await Refusal(Guid.NewGuid(), LevelTradeStrategyBlockFactory.EntryTag)).ShouldBeNull();
    }

    [Test]
    public async Task ATradeWhosePriceHasCrossedItsStopIsRefused()
    {
        BotOwner owner = await SetUp("TRADEFUSDT");

        // Within 5 % of the entry, but below the stop: a bot on the pair saw 98.8 after the snapshot's 100.
        await Price("TRADEFUSDT", price: 98.8m);
        HttpResponseMessage crossed = await owner.Client.PostAsJsonAsync(
            $"/api/scanner/{Venue}/trades",
            new { symbol = "TRADEFUSDT", timeframe = "1h", scenario = "Levels", entry = "100", stop = "99", target = "103" });

        crossed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BotApi.Code(crossed)).ShouldBe("scan_trade_entry_beyond_levels");
        (await Trades(owner, "TRADEFUSDT")).ShouldBeEmpty();
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

    private static async Task<ScanTradeIdeasResponse> Ideas(BotOwner owner, string symbol, string? preset = null)
    {
        string query = preset is null ? string.Empty : $"&preset={Uri.EscapeDataString(preset)}";

        return (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol={symbol}{query}", WebJson))!;
    }

    private static async Task<IReadOnlyList<ScanLiveTradeResponse>> Trades(BotOwner owner, string symbol)
    {
        return (await owner.Client.GetFromJsonAsync<List<ScanLiveTradeResponse>>($"/api/scanner/{Venue}/trades?symbol={symbol}", WebJson))!;
    }

    private static decimal Number(string value)
    {
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     A completed backtest of the owner's preset on the venue over the last 30 days whose cards ended as <paramref name="odds" />.
    /// </summary>
    private async Task<Guid> StoreBacktest(BotOwner owner, ScanCardOdds odds, string preset = "Шансы")
    {
        using IServiceScope scope = _api.Services.CreateScope();
        Guid ownerId;

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            ownerId = await connection.ExecuteScalarAsync<Guid>("SELECT user_id FROM exchange_credentials WHERE id = @Id", new { Id = owner.CredentialId });
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ScanBacktest backtest = ScanBacktest.Start(
            new UserId(ownerId),
            new VenueId(Venue),
            new ScanPresetName(preset),
            new ScanFilter([], HideStale: false),
            now.AddDays(-30),
            now,
            new BackgroundJobId(Guid.NewGuid()),
            now);
        backtest.Complete(new ScanBacktestResult(Pairs: 1, [], ScanStudy.Summarize([], shorts: false, [odds]), [], SignalsTruncated: false), now);
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IScanBacktestRepository>().Add(backtest, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return backtest.Id.Value;
    }

    private async Task<BotOwner> SetUp(string symbol, decimal? spread = null, decimal? natr = null)
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        (await owner.Client.PutAsJsonAsync($"/api/scanner/{Venue}/trade-settings", new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using IServiceScope scope = _api.Services.CreateScope();
        var row = new ScanSnapshotRowInternal(symbol, new ScanMetrics { Price = 100m, Spread = spread, Natr24h = natr }, Incomplete: false);
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

    private async Task Price(string symbol, decimal price)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ILiveMarkPriceRepository>()
            .Store(new LiveMarkPrice(new VenueId(Venue), new InstrumentSymbol(symbol), price, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    private async Task<string?> Refusal(Guid botId, string tag)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        ScanTradeEntryGuard guard = scope.ServiceProvider.GetServices<IOrderSubmitGuard>().OfType<ScanTradeEntryGuard>().Single();
        var order = new OrderSnapshot(
            Id: 1,
            tag,
            OrderSide.Buy,
            OrderType.Limit,
            Quantity: null,
            LimitPrice: 100m,
            QuoteBudget: 50m,
            Reservation: 50m,
            "entry",
            EligibleFromCandleIndex: 1,
            OrderStatus.PendingSubmit);

        return await guard.Refusal(new BotId(botId), order, CancellationToken.None);
    }

    private async Task<bool> Track(Guid tradeId)
    {
        using IServiceScope scope = _api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TrackScanTradeCommand(tradeId));
    }
}