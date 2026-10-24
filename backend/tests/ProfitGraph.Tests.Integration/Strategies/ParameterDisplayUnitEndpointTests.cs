using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

[NonParallelizable]
public sealed class ParameterDisplayUnitEndpointTests
{
    [TestCase("percent", StrategyParameterDisplayUnit.Percent)]
    [TestCase("quote", StrategyParameterDisplayUnit.Quote)]
    [TestCase("base", StrategyParameterDisplayUnit.Base)]
    [TestCase("none", StrategyParameterDisplayUnit.None)]
    public async Task DisplayUnitPersistsAndRoundTripsThroughCreateSaveAndVersion(string unit, StrategyParameterDisplayUnit expected)
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        JsonObject request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!.AsObject();
        request["definition"]!["parameters"]![0]!["displayUnit"] = unit;
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/strategies", request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var result = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        var id = result["id"]!.GetValue<Guid>();
        result["definition"]!["parameters"]![0]!["displayUnit"]!.GetValue<string>().ShouldBe(unit);
        using IServiceScope scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        (await repository.GetVersion(new StrategyId(id), StrategyVersionNumber.First, CancellationToken.None))!.Definition.Parameters[0].DisplayUnit.ShouldBe(expected);
        request["expectedVersion"] = 1;
        request["definition"]!["parameters"]![0]!["displayUnit"] = "none";
        using HttpResponseMessage saved = await client.PutAsJsonAsync($"/api/strategies/{id}", request);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Content.ReadFromJsonAsync<JsonNode>())!["definition"]!["parameters"]![0]!["displayUnit"]!.GetValue<string>().ShouldBe("none");
        var original = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}/versions/1"))!;
        original["definition"]!["parameters"]![0]!["displayUnit"]!.GetValue<string>().ShouldBe(unit);
    }

    [Test]
    public async Task LegacyAbsentUnitRemainsDistinctFromExplicitNone()
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/strategies", StrategyRequestGenerator.Create());
        var id = (await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
        using IServiceScope scope = app.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
        await connection.ExecuteAsync(
            "UPDATE strategy_versions SET snapshot_json=snapshot_json #- '{definition,parameters,0,displayUnit}' WHERE strategy_id=@id;",
            new { id });
        var legacy = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}"))!;
        legacy["definition"]!["parameters"]![0]!["displayUnit"].ShouldBeNull();
        var repository = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        (await repository.GetVersion(new StrategyId(id), StrategyVersionNumber.First, CancellationToken.None))!.Definition.Parameters[0].DisplayUnit.ShouldBeNull();
    }

    [TestCase("Percent")]
    [TestCase("currency")]
    [TestCase("1")]
    public async Task UnknownUnitReturnsFieldValidation(string unit)
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        JsonObject request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!.AsObject();
        request["definition"]!["parameters"]![0]!["displayUnit"] = unit;
        using HttpResponseMessage result = await client.PostAsJsonAsync("/api/strategies", request);
        result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await result.Content.ReadAsStringAsync()).ShouldContain("definition.parameters.displayUnit");
    }

    [Test]
    public async Task TemplatesDeclareAllShareParametersWithoutMarkingMultipliersAsPercent()
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;

        foreach (JsonNode? template in templates)
        {
            foreach (JsonNode? parameter in template!["definition"]!["parameters"]!.AsArray())
            {
                var name = parameter!["name"]!.GetValue<string>();
                var unit = parameter["displayUnit"]!.GetValue<string>();
                string expected = name is "drop" or "rise" or "takeProfit" or "impulse" or "stopLoss" ? "percent"
                    : parameter["valueType"]!.GetValue<string>() == "QuoteAmount" ? "quote" : "none";
                unit.ShouldBe(expected, $"{template["kind"]}: {name}");
            }
        }
    }
}