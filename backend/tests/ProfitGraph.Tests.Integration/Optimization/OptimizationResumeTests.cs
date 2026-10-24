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
///     Pausing, resuming and Worker restarts of optimizations: a search that continues from its stored trials ends with
///     exactly the trials of an uninterrupted one (Profit Fox Designer ADR 0044).
/// </summary>
[NonParallelizable]
public sealed class OptimizationResumeTests
{
    // Large enough that a pause or a Worker restart lands while the search still runs, even on a fast machine.
    private const int TrialCount = 760;
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
    public async Task APausedSearchKeepsItsTrials_AndResumingEndsItLikeAnUninterruptedSearch()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid expected = await Start(strategyId);
        Guid paused = await Start(strategyId);
        using IHost worker = await StartWorker();
        await JobScenario.WaitFor(_client, expected, BacktestScenario.Finished);

        await WaitForTrials(paused);
        HttpResponseMessage pause = await _client.PostAsync($"/api/optimizations/{paused}/pause", content: null);
        pause.StatusCode.ShouldBe(HttpStatusCode.Accepted, await pause.Content.ReadAsStringAsync());
        JsonNode stopped = await JobScenario.WaitFor(_client, paused, JobScenario.Status("Paused"));
        int stored = (await Trials(paused)).Length;
        stopped["pauseRequested"]!.GetValue<bool>().ShouldBeFalse();
        stored.ShouldBeInRange(from: 1, TrialCount - 1);
        (await _client.PostAsync($"/api/optimizations/{paused}/pause", content: null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await _client.PostAsync($"/api/optimizations/{paused}/resume", content: null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonNode job = await JobScenario.WaitFor(_client, paused, BacktestScenario.Finished);
        await worker.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        await AssertSameTrials(paused, expected);
    }

    [Test]
    public async Task AWorkerRestartQueuesTheSearchAgain_AndItContinuesFromItsStoredTrials()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid expected = await Start(strategyId);
        Guid interrupted = await Start(strategyId);
        IHost first = await StartWorker();
        await JobScenario.WaitFor(_client, expected, BacktestScenario.Finished);
        await WaitForTrials(interrupted);

        await first.StopAsync();
        first.Dispose();
        var queued = (await _client.GetFromJsonAsync<JsonNode>($"/api/jobs/{interrupted}"))!;
        queued["status"]!.GetValue<string>().ShouldBe("Queued", queued.ToJsonString());
        int stored = (await Trials(interrupted)).Length;
        stored.ShouldBeInRange(from: 1, TrialCount - 1);

        using IHost second = await StartWorker();
        JsonNode job = await JobScenario.WaitFor(_client, interrupted, BacktestScenario.Finished);
        await second.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        await AssertSameTrials(interrupted, expected);
    }

    [Test]
    public async Task ASearchThatStoresNothingInThreeAttemptsInARowFails()
    {
        Guid id = await Start(await BacktestScenario.CreateStrategy(_application, _userId));

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using IHost worker = await StartWorker(holdHistory: true);
            await JobScenario.WaitFor(_client, id, JobScenario.Status("Running"));
            await worker.StopAsync();
        }

        var job = (await _client.GetFromJsonAsync<JsonNode>($"/api/jobs/{id}"))!;
        job["status"]!.GetValue<string>().ShouldBe("Failed", job.ToJsonString());
        job["failure"]!["code"]!.GetValue<string>().ShouldBe("background_job_interrupted");
        job["failure"]!["message"]!.GetValue<string>().ShouldContain("without making progress");
    }

    [Test]
    public async Task AQueuedSearchPausesAtOnce_AndAPausedOneCanBeCancelled()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid id = await Start(strategyId);

        (await Post(id, "pause"))["status"]!.GetValue<string>().ShouldBe("Paused");
        (await Post(id, "resume"))["status"]!.GetValue<string>().ShouldBe("Queued");
        (await _client.PostAsync($"/api/optimizations/{id}/resume", content: null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Post(id, "pause"))["status"]!.GetValue<string>().ShouldBe("Paused");
        (await _client.PostAsync($"/api/jobs/{id}/cancel", content: null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await _client.GetFromJsonAsync<JsonNode>($"/api/jobs/{id}"))!["status"]!.GetValue<string>().ShouldBe("Cancelled");
        (await _client.PostAsync($"/api/optimizations/{id}/resume", content: null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using HttpClient other = await _application.CreateSignedInClient();
        (await other.PostAsync($"/api/optimizations/{id}/pause", content: null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     Delivery order differs between attempts; what each trial index stored must not.
    /// </summary>
    private async Task AssertSameTrials(Guid actual, Guid expected)
    {
        JsonNode[] resumed = await Trials(actual);
        JsonNode[] uninterrupted = await Trials(expected);
        resumed.Select(trial => trial["sequence"]!.GetValue<long>()).ShouldBe(Enumerable.Range(start: 1, TrialCount).Select(index => (long)index));
        Keys(resumed).ShouldBe(Keys(uninterrupted));

        var optimization = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{actual}"))!;
        var reference = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{expected}"))!;
        optimization["counters"]!.ToJsonString().ShouldBe(reference["counters"]!.ToJsonString());
        optimization["best"]!.ToJsonString().ShouldBe(reference["best"]!.ToJsonString());
        optimization["finalists"]!.AsArray().Count.ShouldBe(2);
    }

    private static string[] Keys(IEnumerable<JsonNode> trials)
    {
        return
        [
            .. trials.OrderBy(trial => trial["trialIndex"]!.GetValue<int>())
                .Select(trial => $"{trial["trialIndex"]}|{trial["status"]}|{trial["objective"]}|{trial["parameters"]!.ToJsonString()}")
        ];
    }

    private async Task<JsonNode[]> Trials(Guid id)
    {
        var trials = new List<JsonNode>();
        long after = 0;

        while (true)
        {
            JsonNode page = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}/trials?after={after}&limit=500"))!;
            trials.AddRange(page["items"]!.AsArray().Select(item => item!));

            if (page["next"]?.GetValue<long>() is not { } next)
            {
                return [.. trials];
            }

            after = next;
        }
    }

    private async Task<JsonNode> Post(Guid id, string action)
    {
        HttpResponseMessage response = await _client.PostAsync($"/api/optimizations/{id}/{action}", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private async Task<Guid> Start(Guid strategyId)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/optimizations",
            new
            {
                backtest = BacktestScenario.Request(strategyId, _symbol),
                ranges = new Dictionary<string, object>
                {
                    ["fastPeriod"] = new { min = "2", max = "20", step = "1" },
                    ["slowPeriod"] = new { min = "21", max = "60", step = "1" }
                },
                method = "grid",
                seed = "3",
                finalistCount = 2,
                minExecutions = 0
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    private async Task WaitForTrials(Guid id)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);

        while ((await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}/trials?limit=1"))!["items"]!.AsArray().Count == 0)
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, $"Optimization {id} stored no trial.");
            await Task.Delay(millisecondsDelay: 10);
        }
    }

    /// <param name="holdHistory">Holds every history request until the Worker stops, so an attempt stores no trial.</param>
    private static async Task<IHost> StartWorker(bool holdHistory = false)
    {
        IHost worker = WorkerHost.Create(settings: new Dictionary<string, string?> { ["Optimization:MaxWorkers"] = "1", ["Optimization:BatchSize"] = "1" });
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = BacktestScenario.Wave;
        history.BeforeGet = holdHistory ? token => Task.Delay(Timeout.Infinite, token) : null;
        await worker.StartAsync();

        return worker;
    }
}