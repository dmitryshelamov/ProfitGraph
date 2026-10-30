using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     A scanner backtest end to end (ADR 0059, section 6): the API stores it and queues its job, the Worker replays every pair of the
///     test venue's invented history, a pair whose history cannot be read is listed as failed, and the result shows the one signal of
///     the climb with its outcomes. No venue is contacted.
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