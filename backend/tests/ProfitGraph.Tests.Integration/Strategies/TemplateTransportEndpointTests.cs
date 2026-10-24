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
public sealed class TemplateTransportEndpointTests
{
    [TestCase("SmaCrossover")]
    [TestCase("Ladder")]
    public async Task MarkerAndBlockDefinitionRoundtripWithoutRewritingJsonb(string marker)
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        JsonNode request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        request["definition"]!["templateKind"] = marker;
        using HttpResponseMessage create = await client.PostAsJsonAsync("/api/strategies", request);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        JsonNode created = (await create.Content.ReadFromJsonAsync<JsonNode>()).ShouldNotBeNull();
        var id = created["id"]!.GetValue<Guid>();
        using IServiceScope scope = app.Services.CreateScope();
        string connectionString = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Postgres").ShouldNotBeNull();
        await using var connection = new NpgsqlConnection(connectionString);
        var before = await connection.QuerySingleAsync<string>("SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id = @id", new { id });
        JsonNode loaded = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}")).ShouldNotBeNull();
        loaded["definition"]!["templateKind"]!.GetValue<string>().ShouldBe(marker);
        JsonNode.DeepEquals(created["definition"], loaded["definition"]).ShouldBeTrue();
        loaded.AsObject().ContainsKey("graph").ShouldBeFalse();
        var after = await connection.QuerySingleAsync<string>("SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id = @id", new { id });
        after.ShouldBe(before);
        JsonNode snapshot = JsonNode.Parse(before).ShouldNotBeNull();
        snapshot["formatVersion"]!.GetValue<int>().ShouldBe(1);
        snapshot["definition"]!["templateKind"]!.GetValue<string>().ShouldBe(marker);
        snapshot.AsObject().ContainsKey("graph").ShouldBeFalse();
    }

    [TestCase("Unknown")]
    [TestCase("ladder")]
    [TestCase("0")]
    public async Task ClosedMarkerRejectsMalformedTransport(string marker)
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        JsonNode request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        request["definition"]!["templateKind"] = marker;
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}