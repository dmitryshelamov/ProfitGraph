using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

[NonParallelizable]
public sealed class StrategyEditorApiTests
{
    [Test]
    public async Task ListOrdersByLastSaveAndReadsCurrentTemplateMarker()
    {
        var clock = new TestClock();
        await using var app = new ProfitGraphApiFactory(clock);
        using HttpClient client = await app.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        JsonNode first = await Create(client, templates[0]!);
        clock.Now = clock.Now.AddSeconds(1);
        JsonNode second = await Create(client, templates[1]!);
        var list = (await client.GetFromJsonAsync<JsonArray>("/api/strategies"))!;
        list.Select(item => item!["id"]!.GetValue<Guid>()).ShouldBe([second["id"]!.GetValue<Guid>(), first["id"]!.GetValue<Guid>()]);
        list[0]!["templateKind"]!.GetValue<string>().ShouldBe("Ladder");
        clock.Now = clock.Now.AddSeconds(1);
        first["expectedVersion"] = 1;
        first["definition"]!["templateKind"] = null;
        using HttpResponseMessage save = await client.PutAsJsonAsync($"/api/strategies/{first["id"]}", first);
        save.StatusCode.ShouldBe(HttpStatusCode.OK);
        list = (await client.GetFromJsonAsync<JsonArray>("/api/strategies"))!;
        list[0]!["id"]!.GetValue<Guid>().ShouldBe(first["id"]!.GetValue<Guid>());
        list[0]!["currentVersion"]!.GetValue<int>().ShouldBe(2);
        list[0]!["templateKind"].ShouldBeNull();
    }

    [Test]
    public async Task ListCapsResultsAtFiveHundred()
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        JsonNode original = await Create(client, templates[0]!);
        using IServiceScope scope = app.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
        await connection.ExecuteAsync(
            sql: """
                 WITH copies AS (
                     INSERT INTO strategies (id, owner_id, name, current_version, created_at, updated_at)
                     SELECT gen_random_uuid(), owner_id, 'copy', 1, created_at, updated_at FROM strategies, generate_series(1, 505) WHERE id=@id
                     RETURNING id, created_at
                 )
                 INSERT INTO strategy_versions (id, strategy_id, version_number, snapshot_json, created_at)
                 SELECT gen_random_uuid(), copies.id, 1, source.snapshot_json, copies.created_at
                 FROM copies CROSS JOIN strategy_versions source WHERE source.strategy_id=@id AND source.version_number=1
                 """,
            new { id = original["id"]!.GetValue<Guid>() });
        (await client.GetFromJsonAsync<JsonArray>("/api/strategies"))!.Count.ShouldBe(500);
    }

    [Test]
    public async Task ValidationUsesUnsavedDraftWithoutCreatingVersion()
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        JsonNode created = await Create(client, templates[0]!);
        created["definition"]!["parameters"]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "fastPeriod")!["defaultValue"] = "0";
        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/strategies/{created["id"]}/validate", new { definition = created["definition"] });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["version"].ShouldBeNull();
        validation["isValid"]!.GetValue<bool>().ShouldBeFalse();
        JsonNode diagnostic = validation["diagnostics"]!.AsArray().First()!;
        diagnostic["code"]!.GetValue<string>().ShouldBe("InvalidParameter");
        diagnostic["parameterName"]!.GetValue<string>().ShouldBe("fastPeriod");
        using HttpResponseMessage savedValidation = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        (await savedValidation.Content.ReadFromJsonAsync<JsonNode>())!["isValid"]!.GetValue<bool>().ShouldBeTrue();
        (await client.GetFromJsonAsync<JsonArray>($"/api/strategies/{created["id"]}/versions"))!.Count.ShouldBe(1);
    }

    [Test]
    public async Task SavedAndDraftValidationUseTheSameStrictSemantics()
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        JsonNode created = await Create(client, templates[0]!);
        created["definition"]!["parameters"]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "fastPeriod")!["defaultValue"] = "0";
        created["expectedVersion"] = 1;
        using HttpResponseMessage save = await client.PutAsJsonAsync($"/api/strategies/{created["id"]}", created);
        save.StatusCode.ShouldBe(HttpStatusCode.OK);
        using HttpResponseMessage draftResponse = await client.PostAsJsonAsync($"/api/strategies/{created["id"]}/validate", new { definition = created["definition"] });
        using HttpResponseMessage savedResponse = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        draftResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        savedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var draft = (await draftResponse.Content.ReadFromJsonAsync<JsonNode>())!;
        var saved = (await savedResponse.Content.ReadFromJsonAsync<JsonNode>())!;
        draft["version"].ShouldBeNull();
        saved["version"]!.GetValue<int>().ShouldBe(2);
        saved["isValid"]!.GetValue<bool>().ShouldBeFalse();
        JsonNode.DeepEquals(draft["diagnostics"], saved["diagnostics"]).ShouldBeTrue();
        using HttpResponseMessage originalResponse = await client.PostAsync($"/api/strategies/{created["id"]}/validate?version=1", content: null);
        var original = (await originalResponse.Content.ReadFromJsonAsync<JsonNode>())!;
        original["version"]!.GetValue<int>().ShouldBe(1);
        original["isValid"]!.GetValue<bool>().ShouldBeTrue();
        (await client.GetFromJsonAsync<JsonArray>($"/api/strategies/{created["id"]}/versions"))!.Count.ShouldBe(2);
    }

    [Test]
    public async Task ValidationRejectsAmbiguousSourcesAndPreservesNotFoundErrors()
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        JsonNode created = await Create(client, templates[0]!);
        using HttpResponseMessage ambiguous = await client.PostAsJsonAsync(
            $"/api/strategies/{created["id"]}/validate?version=1",
            new { definition = created["definition"] });
        ambiguous.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using HttpResponseMessage missingVersion = await client.PostAsync($"/api/strategies/{created["id"]}/validate?version=99", content: null);
        missingVersion.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using HttpResponseMessage missingStrategy = await client.PostAsJsonAsync(
            $"/api/strategies/{Guid.NewGuid()}/validate",
            new { definition = created["definition"] });
        missingStrategy.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task HttpRejectsQuotedLayoutCoordinatesAndKeepsExactDecimalStrings()
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        JsonNode request = templates[0]!.DeepClone();
        request["layout"]!["positions"] = JsonNode.Parse("""{"clock":{"x":"2","y":0}}""");
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        templates[0]!["definition"]!["parameters"]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "fastPeriod")!["defaultValue"]!
            .GetValue<string>()
            .ShouldBe("20");
    }

    private static async Task<JsonNode> Create(HttpClient client, JsonNode template)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", template);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }
}