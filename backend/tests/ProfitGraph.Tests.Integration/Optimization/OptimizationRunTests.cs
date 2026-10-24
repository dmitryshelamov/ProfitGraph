using System.Globalization;
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
///     Optimizations started in the API, searched in the Worker's optimization lane and finished by finalist runs in its
///     main lane (ADR 0013).
/// </summary>
[NonParallelizable]
public sealed class OptimizationRunTests
{
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
    public async Task GridSearchStoresEveryTrial_AndStartsTheBestAsFinalistRuns()
    {
        using IHost worker = await StartWorker();
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/optimizations", Request(strategyId, Ranges("3", "5"), "grid"));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
        response.Headers.Location!.ToString().ShouldEndWith($"/api/jobs/{id}");
        JsonNode job = await JobScenario.WaitFor(_client, id, BacktestScenario.Finished);
        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        job["result"]!["executedTrials"]!.GetValue<int>().ShouldBe(4);
        job["result"]!["finalists"]!.GetValue<int>().ShouldBe(2);

        var optimization = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}"))!;
        var trials = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}/trials"))!;
        JsonArray items = trials["items"]!.AsArray();
        Guid[] finalists = [.. optimization["finalists"]!.AsArray().Select(finalist => finalist!.GetValue<Guid>())];
        JsonNode[] completed = [.. items.Select(item => item!).Where(item => item["status"]!.GetValue<string>() == "completed")];

        // Equal objectives rank in canonical grid order, which is the order of trial indexes in a grid search.
        JsonNode best = completed.OrderByDescending(item => decimal.Parse(item["objective"]!.GetValue<string>(), CultureInfo.InvariantCulture))
            .ThenBy(item => item["trialIndex"]!.GetValue<int>())
            .First();
        optimization.ShouldSatisfyAllConditions(
            () => optimization["stage"]!.GetValue<string>().ShouldBe("starting_finalists"),
            () => optimization["configuration"]!["method"]!.GetValue<string>().ShouldBe("grid"),
            () => optimization["configuration"]!["seed"]!.GetValue<string>().ShouldBe("42"),
            () => optimization["plannedTrials"]!.GetValue<int>().ShouldBe(4),
            () => optimization["history"]!["replayCount"]!.GetValue<int>().ShouldBe(240),
            () => optimization["best"]!["trialIndex"]!.GetValue<int>().ShouldBe(best["trialIndex"]!.GetValue<int>()),
            () => finalists.Length.ShouldBe(2));
        items.Select(item => item!["sequence"]!.GetValue<long>()).ShouldBe([1L, 2L, 3L, 4L]);
        items.Select(item => item!["trialIndex"]!.GetValue<int>()).Order().ShouldBe([0, 1, 2, 3]);
        trials["next"].ShouldBeNull();
        completed.Length.ShouldBeGreaterThanOrEqualTo(expected: 2, "The wave must keep at least two trials within the constraints.");

        foreach (Guid finalist in finalists)
        {
            (await JobScenario.WaitFor(_client, finalist, BacktestScenario.Finished))["status"]!.GetValue<string>().ShouldBe("Completed");
        }

        await worker.StopAsync();
        var first = (await _client.GetFromJsonAsync<JsonNode>($"/api/backtests/{finalists[0]}"))!;
        JsonNode parameters = first["configuration"]!["parameters"]!;
        parameters["fastPeriod"]!.GetValue<string>().ShouldBe(best["parameters"]!["fastPeriod"]!.GetValue<string>());
        parameters["slowPeriod"]!.GetValue<string>().ShouldBe(best["parameters"]!["slowPeriod"]!.GetValue<string>());
        parameters["buyAmount"]!.GetValue<string>().ShouldBe("50");
        first["configuration"]!["from"]!.GetValue<DateTimeOffset>().ShouldBe(BacktestScenario.From);
        first["history"]!["replayCount"]!.GetValue<int>().ShouldBe(240);

        var list = (await _client.GetFromJsonAsync<JsonNode>($"/api/strategies/{strategyId}/optimizations"))!;
        JsonNode summary = list["items"]!.AsArray().Single()!;
        summary["id"]!.GetValue<Guid>().ShouldBe(id);
        summary["executedTrials"]!.GetValue<int>().ShouldBe(4);
        summary["status"]!.GetValue<string>().ShouldBe("Completed");
    }

    [Test]
    public async Task CancellingDuringTheSearchKeepsTheStoredTrials()
    {
        using IHost worker = await StartWorker(new Dictionary<string, string?> { ["Optimization:MaxWorkers"] = "1", ["Optimization:BatchSize"] = "1" });
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid id = await Start(Request(strategyId, Ranges("40", slowFrom: "41", slowTo: "90"), "grid"));

        await WaitForTrials(id);
        (await _client.PostAsync($"/api/jobs/{id}/cancel", content: null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonNode job = await JobScenario.WaitFor(_client, id, BacktestScenario.Finished);
        await worker.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Cancelled", job.ToJsonString());
        var optimization = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}"))!;
        var trials = (await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}/trials?limit=500"))!;
        JsonNode counters = optimization["counters"]!;
        int stored = counters["completed"]!.GetValue<int>() + counters["rejectedByConstraint"]!.GetValue<int>() +
            counters["rejectedByTemplate"]!.GetValue<int>() + counters["failed"]!.GetValue<int>();
        stored.ShouldBeGreaterThan(0);
        stored.ShouldBeLessThan(2000);
        optimization["stage"]!.GetValue<string>().ShouldBe("searching");
        optimization["finalists"]!.AsArray().ShouldBeEmpty();
        trials["items"]!.AsArray().Count.ShouldBe(Math.Min(stored, val2: 500));
    }

    [Test]
    public async Task ABacktestRunsInTheMainLaneWhileAnOptimizationSearches()
    {
        using IHost worker = await StartWorker(new Dictionary<string, string?> { ["Optimization:MaxWorkers"] = "1", ["Optimization:BatchSize"] = "1" });
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid optimization = await Start(Request(strategyId, Ranges("40", slowFrom: "41", slowTo: "90"), "grid"));
        await WaitForTrials(optimization);

        Guid backtest = await BacktestScenario.Start(_client, BacktestScenario.Request(strategyId, _symbol));
        JsonNode run = await JobScenario.WaitFor(_client, backtest, BacktestScenario.Finished);
        var searching = (await _client.GetFromJsonAsync<JsonNode>($"/api/jobs/{optimization}"))!;
        (await _client.PostAsync($"/api/jobs/{optimization}/cancel", content: null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await JobScenario.WaitFor(_client, optimization, BacktestScenario.Finished);
        await worker.StopAsync();

        run["status"]!.GetValue<string>().ShouldBe("Completed", run.ToJsonString());
        searching["status"]!.GetValue<string>().ShouldBe("Running");
    }

    [TestCase("grid", "200", "Budget", "invalid_optimization")]
    [TestCase("auto", "5", "Ranges.missing", "unknown_parameter")]
    [TestCase("auto", "5", "Ranges.buyAmount", "parameter_fixed_and_ranged")]
    public async Task InvalidSearchesAreRejectedOnTheirField(string method, string slowTo, string field, string code)
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Dictionary<string, object> ranges = Ranges("3", slowTo);

        if (field == "Ranges.missing")
        {
            ranges["missing"] = new { min = "1", max = "2", step = "1" };
        }
        else if (field == "Ranges.buyAmount")
        {
            ranges["buyAmount"] = new { min = "10", max = "20", step = "10" };
        }

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/optimizations", Request(strategyId, ranges, method, budget: 5));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        problem.ToJsonString().ShouldContain(code);
        problem.ToJsonString().ShouldContain(field);
    }

    [Test]
    public async Task AnotherUsersOptimizationIsMissing()
    {
        Guid strategyId = await BacktestScenario.CreateStrategy(_application, _userId);
        Guid id = await Start(Request(strategyId, Ranges("3", "5"), "grid"));
        using HttpClient other = await _application.CreateSignedInClient();

        foreach (string path in new[] { $"/api/optimizations/{id}", $"/api/optimizations/{id}/trials", $"/api/strategies/{strategyId}/optimizations" })
        {
            (await other.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }
    }

    private static Dictionary<string, object> Ranges(string fastTo, string slowTo, string slowFrom = "4")
    {
        return new Dictionary<string, object>
        {
            ["fastPeriod"] = new { min = "2", max = fastTo, step = "1" },
            ["slowPeriod"] = new { min = slowFrom, max = slowTo, step = "1" }
        };
    }

    private object Request(Guid strategyId, Dictionary<string, object> ranges, string method, int budget = 2000)
    {
        return new
        {
            backtest = BacktestScenario.Request(strategyId, _symbol),
            ranges,
            method,
            budget,
            seed = "42",
            finalistCount = 2,
            minExecutions = 0
        };
    }

    private async Task<Guid> Start(object request)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/optimizations", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    private async Task WaitForTrials(Guid id)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);

        while ((await _client.GetFromJsonAsync<JsonNode>($"/api/optimizations/{id}/trials?limit=1"))!["items"]!.AsArray().Count == 0)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Optimization {id} stored no trial.");
            }

            await Task.Delay(millisecondsDelay: 25);
        }
    }

    private static async Task<IHost> StartWorker(IReadOnlyDictionary<string, string?>? settings = null)
    {
        IHost worker = WorkerHost.Create(settings: settings);
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = BacktestScenario.Wave;
        await worker.StartAsync();

        return worker;
    }
}