using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

[TestFixture]
[NonParallelizable]
public sealed class GraphTypingEndpointTests
{
    private static async Task<JsonNode> Create(HttpClient client, object request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);

        return JsonNode.Parse(body)!;
    }

    [Test]
    public async Task IncompatibleCheckUnitsIdentifyVisibleBlockWithoutLoweredIds()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        request["definition"]!["blocks"]![1]!["checks"]![0]!["operands"]!["b"]!["valueType"] = "QuoteAmount";
        JsonNode created = await Create(client, request);
        using HttpResponseMessage response = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["isValid"]!.GetValue<bool>().ShouldBeFalse();
        validation["diagnostics"]!.AsArray()
            .ShouldContain(diagnostic =>
                diagnostic!["code"]!.GetValue<string>() == "IncompatibleUnits" && diagnostic["blockId"] != null && diagnostic["blockId"]!.GetValue<string>() == "signal");
        validation.ToJsonString().ShouldNotContain("$");
    }

    [Test]
    public async Task LegacyPrimitiveStrategyReturnsClearErrorWithoutMigratingOrDeletingSnapshot()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode created = await Create(client, StrategyRequestGenerator.Create());
        var id = created["id"]!.GetValue<Guid>();
        string connectionString = application.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        await using var connection = new NpgsqlConnection(connectionString);
        const string legacy = """{"formatVersion":0,"graph":{"nodes":[],"connections":[]},"layout":{"positions":{}}}""";
        await connection.ExecuteAsync(
            "UPDATE strategy_versions SET snapshot_json = CAST(@json AS jsonb) WHERE strategy_id = @id AND version_number = 1",
            new { id, json = legacy });
        var before = await connection.QuerySingleAsync<string>("SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id=@id", new { id });

        foreach (string path in new[] { $"/api/strategies/{id}", $"/api/strategies/{id}/versions/1" })
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            problem["code"]!.GetValue<string>().ShouldBe("unsupported_strategy_format");
            problem["detail"]!.GetValue<string>().ShouldContain("old format");
            problem["detail"]!.GetValue<string>().ShouldContain("recreated");
        }

        using HttpResponseMessage versionsResponse = await client.GetAsync($"/api/strategies/{id}/versions");
        versionsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonArray versions = (await versionsResponse.Content.ReadFromJsonAsync<JsonNode>())!.AsArray();
        versions.Count.ShouldBe(1);
        versions[0]!["number"]!.GetValue<int>().ShouldBe(1);

        var after = await connection.QuerySingleAsync<string>("SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id=@id", new { id });
        after.ShouldBe(before);
        (await connection.QuerySingleAsync<int>("SELECT count(*)::int FROM strategy_versions WHERE strategy_id=@id", new { id })).ShouldBe(1);
    }

    [Test]
    public async Task UnknownNamedValueIdentifiesVisibleBlock()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        request["definition"]!["blocks"]![1]!["checks"]![0]!["operands"]!["a"]!["name"] = "missing";
        JsonNode created = await Create(client, request);
        using HttpResponseMessage response = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["isValid"]!.GetValue<bool>().ShouldBeFalse();
        validation["diagnostics"]!.AsArray().ShouldContain(diagnostic => diagnostic!["blockId"] != null && diagnostic["blockId"]!.GetValue<string>() == "signal");
    }
}