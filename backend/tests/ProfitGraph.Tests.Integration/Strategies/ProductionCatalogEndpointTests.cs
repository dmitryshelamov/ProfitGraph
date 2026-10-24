using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

[TestFixture]
[NonParallelizable]
public sealed class ProductionCatalogEndpointTests
{
    [TestCase("missing", "signal")]
    [TestCase("source", "missing")]
    public async Task InvalidWireReferencesReturnDiagnosticsInsteadOfServerError(string source, string target)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        request["definition"]!["wires"]![0]!["from"]!["block"] = source;
        request["definition"]!["wires"]![0]!["to"] = target;
        using HttpResponseMessage create = await client.PostAsJsonAsync("/api/strategies", request);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var created = (await create.Content.ReadFromJsonAsync<JsonNode>())!;
        using HttpResponseMessage response = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["isValid"]!.GetValue<bool>().ShouldBeFalse();
        validation["diagnostics"]!.AsArray().ShouldNotBeEmpty();
    }

    [Test]
    public async Task CatalogDescribesUserFacingBlocksChecksAndValueSources()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        var catalog = (await client.GetFromJsonAsync<JsonNode>("/api/block-catalog"))!;
        catalog["blockKinds"]!.AsArray().Select(kind => kind!["key"]!.GetValue<string>()).ShouldBe(["event", "condition", "order", "cancel"]);
        catalog["eventSources"]!.AsArray().ShouldContain(source => source!["key"]!.GetValue<string>() == "OnCandleClosed");
        catalog["checkTypes"]!.AsArray().ShouldContain(check => check!["key"]!.GetValue<string>() == "Compare");
        catalog["valueSources"]!.AsArray().ShouldContain(source => source!["key"]!.GetValue<string>() == "SMA");

        foreach (string section in new[] { "blockKinds", "eventSources", "checkTypes", "orderKinds", "valueSources" })
        {
            catalog[section]!.AsArray().ShouldAllBe(entry => !string.IsNullOrWhiteSpace(entry!["label"]!.GetValue<string>()));
        }

        catalog.ToJsonString().ShouldNotContain("MergeEvents");
        catalog.ToJsonString().ShouldNotContain("composite");
        using HttpResponseMessage obsolete = await client.GetAsync("/api/node-types");
        obsolete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task NonexistentExitIsDiagnosedOnTheVisibleSourceBlock()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        request["definition"]!["wires"]![0]!["from"]!["exit"] = "filled";
        using HttpResponseMessage create = await client.PostAsJsonAsync("/api/strategies", request);
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await create.Content.ReadFromJsonAsync<JsonNode>())!;
        using HttpResponseMessage response = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["isValid"]!.GetValue<bool>().ShouldBeFalse();
        validation["diagnostics"]!.AsArray().ShouldContain(diagnostic => diagnostic!["blockId"] != null && diagnostic["blockId"]!.GetValue<string>() == "source");
    }
}