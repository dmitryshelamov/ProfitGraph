using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using Dapper;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     A scanner backtest end to end (ADR 0059, section 6): the API stores it and queues its job, the Worker replays every pair of the
///     test venue's invented history, a pair whose history cannot be read is listed as failed, and the result shows the one signal of
///     the climb with its outcomes, and a later backtest scores the odds of an earlier period on its cards (ADR 0073). No venue is
///     contacted.
/// </summary>
[NonParallelizable]
public sealed class ScanBacktestTests
{
    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory(services: services => services.AddSingleton<IMarketScanSource, BacktestScanSource>());
        _ = _api.Services;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task TheWorkerReplaysEveryPairAndStoresTheSignalsOfThePeriod()
    {
        using HttpClient client = await _api.CreateSignedInClient();
        using IHost worker = WorkerHost.Create(services: services => services.AddSingleton<IMarketScanSource, BacktestScanSource>());
        await worker.StartAsync();
        var preset = new ScanPresetRequest("Рывок", [new ScanConditionRequest("Change1h", "3", Maximum: null)], "Change1h", SortDescending: true, HideStale: false);

        try
        {
            HttpResponseMessage started = await client.PostAsJsonAsync($"/api/scanner/{TestHistoryVenue.WithHistory}/backtests", preset);
            started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            var queued = (await started.Content.ReadFromJsonAsync<ScanBacktestResponse>())!;
            (queued.To - queued.From).ShouldBe(TimeSpan.FromDays(30));
            queued.CompletedAt.ShouldBeNull();

            ScanBacktestResponse? done = null;
            await RuntimeScenario.Until(
                async () => (done = await client.GetFromJsonAsync<ScanBacktestResponse>($"/api/scanner/backtests/{queued.Id}"))!.CompletedAt is not null,
                "the scanner backtest completes");

            done!.Pairs.ShouldBe(2);
            done.FailedPairs.ShouldBe([BacktestScanSource.Bad]);
            done.Summary!.Signals.ShouldBe(1);
            ScanSignalResponse signal = done.Signals!.ShouldHaveSingleItem();
            signal.Symbol.ShouldBe(BacktestScanSource.Good);
            DateTimeOffset climb = BacktestScanSource.ClimbStart(queued.To);
            signal.At.ShouldBeInRange(climb, climb.AddHours(1));
            signal.Change24h.ShouldNotBeNull();
            done.Summary.Change24h.Count.ShouldBe(1);
            done.Summary.Scenarios.Select(scenario => scenario.Scenario).ShouldBe(["Levels", "LevelsRewardRisk2", "Target2R", "Pullback", "Extreme"]);

            // The cards' odds are stored apart for the pair's page (ADR 0071, section 1).
            await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
            {
                (await connection.ExecuteScalarAsync<bool>("SELECT card_odds IS NOT NULL FROM scan_backtests WHERE id = @Id", new { queued.Id })).ShouldBeTrue();
            }

            var list = await client.GetFromJsonAsync<List<ScanBacktestListItemResponse>>($"/api/scanner/{TestHistoryVenue.WithHistory}/backtests");
            ScanBacktestListItemResponse item = list!.ShouldHaveSingleItem();
            (item.Id, item.PresetName, item.Signals, item.JobStatus).ShouldBe((queued.Id, "Рывок", (int?)1, "Completed"));
        }
        finally
        {
            await worker.StopAsync();
        }
    }

    [Test]
    public async Task ABacktestScoresTheOddsOfTheOwnersBacktestsOfOtherPeriodsOnItsCards()
    {
        using HttpClient client = await _api.CreateSignedInClient();
        using IHost worker = WorkerHost.Create(services: services => services.AddSingleton<IMarketScanSource, BacktestScanSource>());
        await worker.StartAsync();
        var preset = new ScanPresetRequest("Калибровка", [new ScanConditionRequest("Change1h", "3", Maximum: null)], "Change1h", SortDescending: true, HideStale: false);

        try
        {
            // The first backtest has no other period to be scored against.
            ScanBacktestResponse first = await Completed(client, preset);
            first.Summary!.Calibration.ShouldBeNull();

            // An earlier backtest of the preset whose cards of every kind and band reached the target 40 % of the time (ADR 0073).
            Guid earlier = await StoreEarlier(first, preset.Name);

            ScanBacktestResponse second = await Completed(client, preset);

            ScanCardCalibrationResponse calibration = second.Summary!.Calibration!;
            calibration.Sources.ShouldBe([earlier]);
            calibration.Cards.ShouldBeGreaterThan(0);
            calibration.Bins.Sum(bin => bin.Cards).ShouldBe(calibration.Cards);
            decimal.Parse(calibration.Brier, CultureInfo.InvariantCulture).ShouldBeInRange(from: 0m, to: 1m);
        }
        finally
        {
            await worker.StopAsync();
        }
    }

    private static async Task<ScanBacktestResponse> Completed(HttpClient client, ScanPresetRequest preset)
    {
        var queued = (await (await client.PostAsJsonAsync($"/api/scanner/{TestHistoryVenue.WithHistory}/backtests", preset)).Content
            .ReadFromJsonAsync<ScanBacktestResponse>())!;
        ScanBacktestResponse? done = null;
        await RuntimeScenario.Until(
            async () => (done = await client.GetFromJsonAsync<ScanBacktestResponse>($"/api/scanner/backtests/{queued.Id}"))!.CompletedAt is not null,
            "the scanner backtest completes");

        return done!;
    }

    /// <summary>
    ///     A completed backtest of the owner of <paramref name="later" /> over the 30 days before it whose cards of every kind and band
    ///     filled and reached the target 80 times in 200.
    /// </summary>
    private async Task<Guid> StoreEarlier(ScanBacktestResponse later, string presetName)
    {
        Guid ownerId;

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            ownerId = await connection.ExecuteScalarAsync<Guid>("SELECT owner_id FROM scan_backtests WHERE id = @Id", new { later.Id });
        }

        ScanCardOdds[] odds =
        [
            ..
            from timeframe in new[] { "1m", "15m", "1h", "4h", "1d" }
            from scenario in Enum.GetValues<ScanScenario>()
            from direction in Enum.GetValues<PositionDirection>()
            from chance in Enumerable.Range(start: 0, count: 5)
            from stop in Enumerable.Range(start: 0, count: 6)
            select new ScanCardOdds(timeframe, scenario, direction, chance, stop, Cards: 200, Filled: 200, Targets: 80, Stops: 120, MeanR: 0m, MissedMeanR: -1m)
        ];
        ScanBacktest backtest = ScanBacktest.Start(
            new UserId(ownerId),
            new VenueId(TestHistoryVenue.WithHistory),
            new ScanPresetName(presetName),
            new ScanFilter([], HideStale: false),
            later.From.AddDays(-30),
            later.From,
            new BackgroundJobId(Guid.NewGuid()),
            later.From);
        backtest.Complete(new ScanBacktestResult(Pairs: 1, [], ScanStudy.Summarize([], shorts: false, odds), [], SignalsTruncated: false), later.From);
        using IServiceScope scope = _api.Services.CreateScope();
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

    [Test]
    public async Task ABacktestMayEndDaysBackForAnotherMarket()
    {
        using HttpClient client = await _api.CreateSignedInClient();
        var preset = new ScanPresetRequest("Раньше", [], "Change1h", SortDescending: true, HideStale: true);

        var earlier = (await (await client.PostAsJsonAsync($"/api/scanner/{TestHistoryVenue.WithHistory}/backtests?daysBack=30", preset)).Content
            .ReadFromJsonAsync<ScanBacktestResponse>())!;
        HttpResponseMessage tooFar = await client.PostAsJsonAsync($"/api/scanner/{TestHistoryVenue.WithHistory}/backtests?daysBack=400", preset);

        (earlier.To - earlier.From).ShouldBe(TimeSpan.FromDays(30));
        earlier.To.ShouldBeInRange(DateTimeOffset.UtcNow.AddDays(-30).AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(-30));
        tooFar.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnotherUserCannotReadTheBacktest()
    {
        using HttpClient owner = await _api.CreateSignedInClient();
        using HttpClient stranger = await _api.CreateSignedInClient();
        var preset = new ScanPresetRequest("Мой", [], "Change1h", SortDescending: true, HideStale: true);
        var queued =
            await (await owner.PostAsJsonAsync($"/api/scanner/{TestHistoryVenue.WithHistory}/backtests", preset)).Content.ReadFromJsonAsync<ScanBacktestResponse>();

        HttpResponseMessage foreign = await stranger.GetAsync($"/api/scanner/backtests/{queued!.Id}");

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).ShouldContain("scan_backtest_not_found");
    }
}