using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.Jobs;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     The «Сетка» template from the catalog backtested through the API (ADR 0058): its levels buy and sell on the wave, and a
///     profit step below the round trip's fees is refused before queueing.
/// </summary>
[NonParallelizable]
public sealed class GridBacktestTests
{
    private ProfitGraphApiFactory _application = null!;
    private HttpClient _client = null!;
    private string _symbol = string.Empty;

    [SetUp]
    public async Task StartApi()
    {
        _application = new ProfitGraphApiFactory();
        _client = await _application.CreateSignedInClient();
        _symbol = BacktestScenario.NewSymbol();
    }

    [TearDown]
    public async Task StopApi()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Test]
    public async Task TheGridTemplateTradesItsLevelsOnTheWave()
    {
        Guid strategyId = await CreateFromTemplate();
        using IHost worker = WorkerHost.Create();
        var history = worker.Services.GetRequiredService<FakeHistorySource>();
        history.Reset();
        history.Candles = BacktestScenario.Wave;
        await worker.StartAsync();

        Guid id = await BacktestScenario.Start(_client, Request(strategyId, "0.001"));
        JsonNode job = await JobScenario.WaitFor(_client, id, BacktestScenario.Finished);
        await worker.StopAsync();

        job["status"]!.GetValue<string>().ShouldBe("Completed", job.ToJsonString());
        var run = (await _client.GetFromJsonAsync<JsonNode>($"/api/backtests/{id}"))!;
        run["configuration"]!["limitFill"]!.GetValue<string>().ShouldBe("Through");
        var fills = (await _client.GetFromJsonAsync<JsonNode>($"/api/backtests/{id}/fills?after=0&limit=100"))!;
        string[] tags = [.. fills["items"]!.AsArray().Select(fill => fill!["fill"]!["tag"]!.GetValue<string>())];
        tags.ShouldContain(tag => tag.StartsWith("grid.grid.", StringComparison.Ordinal) && tag.EndsWith(".buy", StringComparison.Ordinal));
        tags.ShouldContain(tag => tag.StartsWith("grid.grid.", StringComparison.Ordinal) && tag.EndsWith(".sell", StringComparison.Ordinal));
        run["result"]!["metrics"]!["full"]!["maxCapitalCommitted"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task AProfitStepBelowTheRoundTripsFeesIsRefused()
    {
        Guid strategyId = await CreateFromTemplate();

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/backtests", Request(strategyId, "0.006"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray()
            .Select(error => error!["code"]!.GetValue<string>())
            .ShouldContain(BacktestErrorCodes.GridProfitBelowFees);
    }

    private async Task<Guid> CreateFromTemplate()
    {
        var templates = (await _client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        JsonNode template = templates.Single(item => item!["kind"]!.GetValue<string>() == "Grid")!;
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/strategies",
            new JsonObject { ["name"] = "Grid", ["definition"] = template["definition"]!.DeepClone(), ["layout"] = template["layout"]!.DeepClone() });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    private object Request(Guid strategyId, string limitFeeRate)
    {
        return new
        {
            strategyId,
            venue = TestHistoryVenue.WithHistory,
            symbol = _symbol,
            timeframe = "1h",
            from = BacktestScenario.From,
            to = BacktestScenario.To,
            parameters = new Dictionary<string, string> { ["gridLevels"] = "8", ["gridStep"] = "0.01", ["gridProfitStep"] = "0.01" },
            startingCapital = "1000",
            limitFeeRate,
            marketFeeRate = limitFeeRate
        };
    }
}