using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ProfitGraph.Tests.Integration.Common;

/// <summary>
///     The frontend generates its API types from this document, so numbers and enums must keep their exact JSON types.
/// </summary>
public sealed class OpenApiDocumentTests
{
    [Test]
    public async Task IntegersAreNotDescribedAsStrings()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = application.CreateClient();

        var document = (await client.GetFromJsonAsync<JsonNode>("/openapi/v1.json"))!;
        JsonNode summary = document["components"]!["schemas"]!["StrategySummaryResponse"]!["properties"]!["currentVersion"]!;

        summary["type"]!.ToJsonString().ShouldBe("\"integer\"");
    }

    [Test]
    public async Task EnumsAreDescribedByName()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = application.CreateClient();

        var document = (await client.GetFromJsonAsync<JsonNode>("/openapi/v1.json"))!;
        JsonArray roles = document["components"]!["schemas"]!["UserRole"]!["enum"]!.AsArray();

        roles.Select(role => role!.GetValue<string>()).ShouldBe(["Owner", "User"]);
    }
}