using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Optimization;

/// <summary>
///     Walk-forward optimizations through API and Worker (ADR 0014); the stage 4 criterion is that one completes and
///     resumes after a Worker restart with the result of an uninterrupted one.
/// </summary>
[NonParallelizable]
public sealed class WalkForwardOptimizationTests
{
    private const int Windows = 3;
    private ProfitGraphApiFactory _application = null!;
    private HttpClient _client = null!;
    private Guid _userId;
    private string _symbol = string.Empty;

    [SetUp]
    public async Task StartApi()
    {
        _application = new ProfitGraphApiFactory();
        _client = await _application.CreateSignedInClient();
        _userId = await JobScenario.UserId(_client);
        _symbol = BacktestScenario.NewSymbol();
    }

    [TearDown]
    public async Task StopApi()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Test]
    public async Task WalkForwardSearchesEveryWindow_TestsItsWinner_AndStartsTheLastWinner()
    {
        using IHost worker = await StartWorker();
        Guid id = await Start(await BacktestScenario.CreateStrategy(_application, _userId), "5", "6", "9");

        JsonNode job = await JobScenario.WaitFor(_client, id, BacktestScenario.Finished);
        await worker.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        var optimization = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}"))!;
        JsonNode[] windows = await WindowsOf(id);
        JsonNode[] trials = await TrialsOf(id);
        optimization["configuration"]!["walkForward"]!["windows"]!.GetValue<int>().ShouldBe(Windows);
        optimization["plannedTrials"]!.GetValue<int>().ShouldBe(Windows * 16);
        windows.Select(window => window["index"]!.GetValue<int>()).ShouldBe([0, 1, 2]);
        windows.ShouldAllBe(window => window["testCount"]!.GetValue<int>() == 36 && window["trainCount"]!.GetValue<int>() == 72);
        windows.Where(window => window["status"]!.GetValue<string>() == "completed")
            .ShouldAllBe(window => window["equity"]!.AsArray().Count == 36 && window["testMetrics"] != null);
        trials.GroupBy(trial => trial["windowIndex"]!.GetValue<int>()).Select(group => group.Count()).ShouldBe([16, 16, 16]);
        JsonNode summary = optimization["walkForwardSummary"]!;
        summary["testWindows"]!.GetValue<int>().ShouldBe(windows.Count(window => window["status"]!.GetValue<string>() == "completed"));
        summary["parameterStability"]!.AsArray().Select(item => item!["parameter"]!.GetValue<string>()).Order().ShouldBe(["fastPeriod", "slowPeriod"]);
        optimization["finalists"]!.AsArray().Count.ShouldBe(windows[^1]["winnerTrialIndex"] is null ? 0 : 1);
    }

    [Test]
    public async Task AWalkForwardSearchInterruptedByAWorkerRestartResumes_AndEndsLikeAnUninterruptedOne()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid expected = await Start(strategyId, "10", "11", "20");
        Guid interrupted = await Start(strategyId, "10", "11", "20");
        IHost first = await StartWorker();
        await JobScenario.WaitFor(_client, expected, BacktestScenario.Finished);
        await WaitFor(interrupted, async () => (await WindowsOf(interrupted)).Length > 0);

        await first.StopAsync();
        first.Dispose();
        (await _client.GetFromJsonAsync<JsonNode>($"/api/jobs/{interrupted}"))!["status"]!.GetValue<string>().ShouldBe("Queued");
        (await WindowsOf(interrupted)).Length.ShouldBeLessThan(Windows);

        using IHost second = await StartWorker();
        JsonNode job = await JobScenario.WaitFor(_client, interrupted, BacktestScenario.Finished);
        await second.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        Keys(await WindowsOf(interrupted)).ShouldBe(Keys(await WindowsOf(expected)));
        TrialKeys(await TrialsOf(interrupted)).ShouldBe(TrialKeys(await TrialsOf(expected)));
        var resumed = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{interrupted}"))!;
        var reference = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{expected}"))!;
        resumed["walkForwardSummary"]!.ToJsonString().ShouldBe(reference["walkForwardSummary"]!.ToJsonString());
        resumed["counters"]!.ToJsonString().ShouldBe(reference["counters"]!.ToJsonString());
        resumed["finalists"]!.AsArray().Count.ShouldBe(reference["finalists"]!.AsArray().Count);
    }

    [Test]
    public async Task WindowsThatDoNotFitTheInSampleRangeAreRejectedAtStart()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/optimizations",
            Request(strategyId, "5", "6", "9", windows: 50, ratio: 20, BacktestScenario.From.AddDays(2)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string problem = await response.Content.ReadAsStringAsync();
        problem.ShouldContain("walk_forward_invalid_windows");
    }

    private static string[] Keys(IEnumerable<JsonNode> windows)
    {
        return
        [
            .. windows.Select(window =>
                $"{window["index"]}|{window["status"]}|{window["winnerTrialIndex"]}|{window["parameters"]?.ToJsonString()}|" +
                $"{window["testMetrics"]?.ToJsonString()}|{window["equity"]!.ToJsonString()}")
        ];
    }

    private static string[] TrialKeys(IEnumerable<JsonNode> trials)
    {
        return
        [
            .. trials.OrderBy(trial => trial["windowIndex"]!.GetValue<int>())
                .ThenBy(trial => trial["trialIndex"]!.GetValue<int>())
                .Select(trial => $"{trial["windowIndex"]}|{trial["trialIndex"]}|{trial["status"]}|{trial["objective"]}|{trial["parameters"]!.ToJsonString()}")
        ];
    }

    private async Task<JsonNode[]> WindowsOf(Guid id)
    {
        return [.. (await _client.GetFromJsonAsync<JsonArray>($"/api/optimizations/{id}/windows"))!.Select(window => window!)];
    }

    private async Task<JsonNode[]> TrialsOf(Guid id)
    {
        var trials = new List<JsonNode>();
        long after = 0;

        while (true)
        {
            var page = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}/trials?after={after}&limit=500"))!;
            trials.AddRange(page["items"]!.AsArray().Select(item => item!));

            if (page["next"]?.GetValue<long>() is not { } next)
            {
                return [.. trials];
            }

            after = next;
        }
    }

    private object Request(Guid strategyId, string fastTo, string slowFrom, string slowTo, int windows = Windows, int ratio = 2, DateTimeOffset? to = null)
    {
        return new
        {
            backtest = BacktestScenario.Request(strategyId, _symbol, to: to),
            ranges = new Dictionary<string, object>
            {
                ["fastPeriod"] = new { min = "2", max = fastTo, step = "1" },
                ["slowPeriod"] = new { min = slowFrom, max = slowTo, step = "1" }
            },
            method = "grid",
            seed = "5",
            minExecutions = 0,
            walkForward = new { windows, trainToTestRatio = ratio, scheme = "rolling" }
        };
    }

    private async Task<Guid> Start(Guid strategyId, string fastTo, string slowFrom, string slowTo)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/optimizations", Request(strategyId, fastTo, slowFrom, slowTo));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    private static async Task WaitFor(Guid id, Func<Task<bool>> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (!await condition())
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, $"Optimization {id} did not reach the expected state.");
            await Task.Delay(millisecondsDelay: 10);
        }
    }

    private static async Task<IHost> StartWorker()
    {
        IHost worker = WorkerHost.Create(settings: new Dictionary<string, string?> { ["Optimization:MaxWorkers"] = "1", ["Optimization:BatchSize"] = "1" });
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = BacktestScenario.Wave;
        await worker.StartAsync();

        return worker;
    }
}