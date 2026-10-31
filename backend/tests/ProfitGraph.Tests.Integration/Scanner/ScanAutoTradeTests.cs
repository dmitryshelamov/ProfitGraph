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
using ProfitGraph.Application.Scanner.AutoTrades;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.FollowScanPaperTrades;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     Auto trades of a preset on paper (ADR 0071, sections 4–7) on the test venue: a rule is saved off, refuses paper without two
///     backtests of opposite markets that prove its card, switches to paper with them, turns a pair entering the preset into a request,
///     the Worker's handling makes the rule's card a paper trade sized by the owner's settings, the scanner's minutes step it, and the
///     page lists all of it. Values are invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class ScanAutoTradeTests
{
    private const string Venue = TestHistoryVenue.WithHistory;
    private const string Symbol = "AUTOAUSDT";
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        // The test venue offers hours and days only, so the cards' levels and trades run on hours.
        Dictionary<string, string> settings = BotApi.Settings();
        settings["ScanTrades:Timeframe"] = "1h";
        settings["ScanTrades:LevelTimeframes:0"] = "1h";
        settings["ScanTrades:LevelTimeframes:1"] = "1d";
        _api = new ProfitGraphApiFactory(settings, services: services => services.AddSingleton<IMarketScanSource, TestScanSource>());
        _api.Services.GetRequiredService<FakeHistorySource>().Candles = ScanLevelsTests.Shaped;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task ARuleProvenByTwoMarketsTradesItsCardOnPaper()
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        (await owner.Client.PutAsJsonAsync($"/api/scanner/{Venue}/trade-settings", new { credentialId = owner.CredentialId, risk = "5", maxAmount = "1000" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var preset = new ScanPresetRequest("Авто", [new ScanConditionRequest("Change1h", "3", Maximum: null)], "Change1h", SortDescending: true, HideStale: false);
        var created = (await (await owner.Client.PostAsJsonAsync($"/api/scanner/{Venue}/presets", preset)).Content.ReadFromJsonAsync<ScanPresetResponse>(WebJson))!;
        var rules = $"/api/scanner/presets/{created.Id}/auto-trades";

        // The rule takes the long level card of hours; it is created off and a daily loss of three risks.
        var saved = (await (await owner.Client.PutAsJsonAsync(rules, new { direction = "Long", scenario = "Levels", timeframe = "1h", minExpectedR = "0.1" }))
            .Content.ReadFromJsonAsync<ScanAutoTradeRuleResponse>(WebJson))!;
        (saved.Mode, saved.DailyLossLimit, saved.MaxOpenTrades).ShouldBe(("Off", "15", 3));

        HttpResponseMessage unproven = await owner.Client.PutAsJsonAsync($"{rules}/mode", new { mode = "Paper" });
        unproven.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await BotApi.Code(unproven)).ShouldBe("scan_auto_trade_not_proven");

        // Two backtests of the preset's conditions, a rising and an earlier falling market, in which the cards earned 0.2 R in each third.
        Guid ownerId = await OwnerId(owner);
        await Snapshot(change1h: 1m);
        ScanTradeIdeaResponse levels = (await owner.Client.GetFromJsonAsync<ScanTradeIdeasResponse>($"/api/scanner/{Venue}/trade-ideas?symbol={Symbol}", WebJson))!
            .Ideas.First(idea => idea is { Timeframe: "1h", Scenario: "Levels" });
        var proposal = new ScanTradeIdea(
            ScanScenario.Levels,
            Number(levels.Entry),
            Number(levels.Stop),
            Number(levels.Target),
            Number(levels.RewardRisk),
            TimeSpan.FromMinutes(levels.EntryValidityMinutes));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await StoreBacktest(ownerId, preset, now.AddDays(-60), now.AddDays(-30), marketChange: -6m, odds: null);
        Guid latest = await StoreBacktest(ownerId, preset, now.AddDays(-30), now, marketChange: 8m, Odds(proposal));

        HttpResponseMessage switched = await owner.Client.PutAsJsonAsync($"{rules}/mode", new { mode = "Paper" });
        var paper = (await switched.Content.ReadFromJsonAsync<ScanAutoTradeRuleResponse>(WebJson))!;
        (paper.Mode, paper.Backtest!.BacktestId, paper.OtherRegime!.Thirds.Sum(third => third.Trades)).ShouldBe(("Paper", latest, 120));

        // The first snapshot after the switch only records; the pair then enters the preset and the Worker takes its card on paper.
        DateTimeOffset entered = await Snapshot(change1h: 1m, now.AddSeconds(1));
        entered = await Snapshot(change1h: 5m, entered.AddSeconds(1));
        (await HandleNext()).ShouldBeTrue();
        (await HandleNext()).ShouldBeFalse();

        var page = (await owner.Client.GetFromJsonAsync<ScanAutoTradeResponse>(rules, WebJson))!;
        ScanAutoTradeRequestResponse request = page.Requests.ShouldHaveSingleItem();
        (request.Symbol, request.Status, request.ErrorCode).ShouldBe((Symbol, "Paper", null));
        ScanPaperTradeResponse trade = page.PaperTrades.ShouldHaveSingleItem();
        (trade.Id, trade.Status, trade.Timeframe, trade.Scenario, trade.Risk).ShouldBe((request.TradeId!.Value, "Waiting", "1h", "Levels", "5"));
        (page.OpenTrades, page.TodayTrades, page.Latest!.BacktestId, page.Latest.Candidates.ShouldHaveSingleItem().Qualifies)
            .ShouldBe((1, 1, latest, true));

        // A minute trading through the entry fills it.
        var minute = new DateTimeOffset(entered.UtcTicks - entered.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
        decimal entry = Number(trade.Entry);
        await Send(
            new FollowScanPaperTradesCommand(
                Venue,
                new Dictionary<string, IReadOnlyList<ScanCandle>>
                {
                    [Symbol] = [new ScanCandle(minute, entry, entry + 0.01m, entry - 0.01m, entry, Volume: 1, Turnover: 1)]
                }));

        ScanPaperTradeResponse open = (await owner.Client.GetFromJsonAsync<ScanAutoTradeResponse>(rules, WebJson))!.PaperTrades.ShouldHaveSingleItem();
        (open.Status, open.EnteredAt).ShouldBe(("Open", minute));

        // Changing the preset's conditions switches the rule off.
        (await owner.Client.PutAsJsonAsync($"/api/scanner/presets/{created.Id}", preset with { Conditions = [new ScanConditionRequest("Change1h", "4", Maximum: null)] }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.GetFromJsonAsync<ScanAutoTradeResponse>(rules, WebJson))!.Rule!.Mode.ShouldBe("Off");
    }

    [Test]
    public async Task AnotherUserSeesNoRuleOfThePreset()
    {
        BotOwner owner = await BotApi.NewOwner(_api);
        BotOwner stranger = await BotApi.NewOwner(_api);
        var preset = new ScanPresetRequest("Чужой", [], "Change1h", SortDescending: true, HideStale: false);
        var created = (await (await owner.Client.PostAsJsonAsync($"/api/scanner/{Venue}/presets", preset)).Content.ReadFromJsonAsync<ScanPresetResponse>(WebJson))!;

        HttpResponseMessage foreign = await stranger.Client.GetAsync($"/api/scanner/presets/{created.Id}/auto-trades");
        HttpResponseMessage noRule = await owner.Client.PutAsJsonAsync($"/api/scanner/presets/{created.Id}/auto-trades/mode", new { mode = "Paper" });

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noRule.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BotApi.Code(noRule)).ShouldBe("scan_auto_trade_not_found");
    }

    private static decimal Number(string value)
    {
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static ScanCardOdds Odds(ScanTradeIdea proposal)
    {
        // 300 of 320 cards like the level card filled, 180 reached the target and the others lost half a risk on average, so even the
        // cautious end of the card's odds expects more than 0.1 R (ADR 0073); a NATR of 4 % bands its stop.
        return new ScanCardOdds(
            "1h",
            ScanScenario.Levels,
            PositionDirection.Long,
            ScanCardBands.Chance(proposal.RewardRisk),
            ScanCardBands.Stop(proposal, natrPercent: 4m)!.Value,
            Cards: 320,
            Filled: 300,
            Targets: 180,
            Stops: 120,
            MeanR: 0.2m,
            MissedMeanR: -0.5m);
    }

    private static async Task<Guid> OwnerId(BotOwner owner)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<Guid>("SELECT user_id FROM exchange_credentials WHERE id = @Id", new { Id = owner.CredentialId });
    }

    /// <summary>
    ///     A completed backtest of the preset's conditions with 40 closed level cards of hours in each third, each 0.2 R at an expected R
    ///     of 0.3.
    /// </summary>
    private async Task<Guid> StoreBacktest(Guid ownerId, ScanPresetRequest preset, DateTimeOffset from, DateTimeOffset to, decimal marketChange, ScanCardOdds? odds)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        var filter = new ScanFilter(
            [.. preset.Conditions.Select(condition => new ScanCondition(Enum.Parse<ScanMetric>(condition.Metric), Number(condition.Minimum!), maximum: null))],
            preset.HideStale);
        ScanBacktest backtest = ScanBacktest.Start(
            new UserId(ownerId),
            new VenueId(Venue),
            new ScanPresetName(preset.Name),
            filter,
            from,
            to,
            new BackgroundJobId(Guid.NewGuid()),
            to);
        backtest.Complete(
            new ScanBacktestResult(Pairs: 1, [], ScanStudy.Summarize([], shorts: false, odds is null ? [] : [odds]), [], SignalsTruncated: false, marketChange),
            to);
        ScanBacktestCard[] cards =
        [
            .. Enumerable.Range(start: 0, count: 120)
                .Select(index => new ScanBacktestCard(
                    "1h",
                    ScanScenario.Levels,
                    PositionDirection.Long,
                    from.AddHours(index),
                    index / 40,
                    ResultR: 0.2m,
                    Chance: 0.5m,
                    ExpectedR: 0.3m))
        ];
        var repository = scope.ServiceProvider.GetRequiredService<IScanBacktestRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await repository.Add(backtest, CancellationToken.None);
                    await repository.AddCards(backtest.Id, cards, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return backtest.Id.Value;
    }

    /// <summary>
    ///     Replaces the venue's snapshot with the pair at a price of 100, a NATR of 4 % and the given hourly change, computed after
    ///     <paramref name="after" />.
    /// </summary>
    private async Task<DateTimeOffset> Snapshot(decimal change1h, DateTimeOffset? after = null)
    {
        DateTimeOffset at = after is { } later && later > DateTimeOffset.UtcNow ? later : DateTimeOffset.UtcNow;
        var row = new ScanSnapshotRowInternal(Symbol, new ScanMetrics { Price = 100m, Natr24h = 4m, Change1h = change1h }, Incomplete: false);
        await Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(Venue, at, [row])));

        return at;
    }

    private async Task Send(IRequest command)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private async Task<bool> HandleNext()
    {
        using IServiceScope scope = _api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ScanAutoTradeRequestHandler>().HandleNext(CancellationToken.None);
    }
}