using System.Globalization;
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

[NonParallelizable]
public sealed class ExactGraphParameterEndpointTests
{
    [TestCase("9007199254740993.123456789", "9007199254740993.123456789")]
    [TestCase("0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    [TestCase("79228162514264337593543950335", "79228162514264337593543950335")]
    [TestCase("1.230000000000000000000000000000000", "1.23")]
    public async Task ParameterAndConstantRoundtripExactlyWithoutRewritingHistoricalVersion(string input, string expected)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = Request(input);
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        var id = created["id"]!.GetValue<string>();
        var loaded = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}"))!;
        loaded["definition"]!["parameters"]![0]!["defaultValue"]!.GetValue<string>().ShouldBe(expected);
        loaded["definition"]!["values"]![0]!["expression"]!["number"]!.GetValue<string>().ShouldBe(expected);
        string connectionString = application.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        await using var connection = new NpgsqlConnection(connectionString);
        var stored = await connection.QuerySingleAsync<string>(
            "SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id=CAST(@id AS uuid) AND version_number=1",
            new { id });
        JsonNode snapshot = JsonNode.Parse(stored)!;
        decimal.Parse(snapshot["definition"]!["parameters"]![0]!["defaultValue"]!.ToString(), CultureInfo.InvariantCulture)
            .ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
        loaded["expectedVersion"] = 1;
        using HttpResponseMessage save = await client.PutAsJsonAsync($"/api/strategies/{id}", loaded);
        save.StatusCode.ShouldBe(HttpStatusCode.OK);
        var immutable = await connection.QuerySingleAsync<string>(
            "SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id=CAST(@id AS uuid) AND version_number=1",
            new { id });
        immutable.ShouldBe(stored);
    }

    [TestCase("0.00000000000000000000000000001")]
    [TestCase("1.23456789012345678901234567895")]
    [TestCase("79228162514264337593543950336")]
    [TestCase("1e-28")]
    [TestCase("1,000")]
    [TestCase("NaN")]
    [TestCase(" 1")]
    public async Task InexactOrMalformedDecimalIsRejectedWithoutCreatingStrategy(string input)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", Request(input));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<JsonArray>("/api/strategies"))!.ShouldBeEmpty();
    }

    [TestCase("true")]
    [TestCase("1")]
    [TestCase("null")]
    public async Task DecimalParameterRequiresJsonString(string literal)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = Request("1");
        request["definition"]!["parameters"]![0]!["defaultValue"] = JsonNode.Parse(literal);
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestCase("number", "true")]
    [TestCase("number", "1")]
    [TestCase("text", "true")]
    [TestCase("text", "1")]
    [TestCase("valueType", "1")]
    [TestCase("valueType", "\"Unknown\"")]
    public async Task MalformedExpressionLiteralShapesAreRejectedWithoutWrites(string field, string literal)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = Request("1");
        request["definition"]!["values"]![0]!["expression"]![field] = JsonNode.Parse(literal);
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<JsonArray>("/api/strategies"))!.ShouldBeEmpty();
    }

    [Test]
    public async Task BooleanOrderIdentityAndTagRetainTypesThroughHttpAndJsonb()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = Request("1");
        request["definition"]!["values"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "enabled", ["expression"] = new JsonObject
                    { ["kind"] = "constant", ["valueType"] = "Boolean", ["text"] = "true" }
            },
            new JsonObject
            {
                ["name"] = "order", ["expression"] = new JsonObject
                    { ["kind"] = "constant", ["valueType"] = "OrderId", ["number"] = "9223372036854775807" }
            },
            new JsonObject
            {
                ["name"] = "tag", ["expression"] = new JsonObject
                    { ["kind"] = "constant", ["valueType"] = "Tag", ["text"] = "grid.entry" }
            });
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        var loaded = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{created["id"]}"))!;
        JsonArray values = loaded["definition"]!["values"]!.AsArray();
        values[0]!["expression"]!["valueType"]!.GetValue<string>().ShouldBe("Boolean");
        values[0]!["expression"]!["text"]!.GetValue<string>().ShouldBe("true");
        values[1]!["expression"]!["valueType"]!.GetValue<string>().ShouldBe("OrderId");
        values[1]!["expression"]!["number"]!.GetValue<string>().ShouldBe("9223372036854775807");
        values[2]!["expression"]!["valueType"]!.GetValue<string>().ShouldBe("Tag");
        values[2]!["expression"]!["text"]!.GetValue<string>().ShouldBe("grid.entry");
        string connectionString = application.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        await using var connection = new NpgsqlConnection(connectionString);
        var json = await connection.QuerySingleAsync<string>(
            "SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id=@id AND version_number=1",
            new { id = created["id"]!.GetValue<Guid>() });
        JsonArray storedValues = JsonNode.Parse(json)!["definition"]!["values"]!.AsArray();
        storedValues[0]!["expression"]!["text"]!.GetValue<string>().ShouldBe("true");
        storedValues[1]!["expression"]!["number"]!.GetValue<decimal>().ShouldBe(9223372036854775807m);
        storedValues[2]!["expression"]!["text"]!.GetValue<string>().ShouldBe("grid.entry");
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("9223372036854775808")]
    public async Task InvalidOrderIdentityDraftIsPreservedButCannotValidate(string number)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = Request("1");
        request["definition"]!["values"]![0]!["expression"] = new JsonObject
            { ["kind"] = "constant", ["valueType"] = "OrderId", ["number"] = number };
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        using HttpResponseMessage validationResponse = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        validationResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await validationResponse.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["isValid"]!.GetValue<bool>().ShouldBeFalse();
        validation["diagnostics"]!.AsArray().ShouldContain(item => item!["valueName"] != null && item["valueName"]!.GetValue<string>() == "constant");
    }

    private static JsonNode Request(string number)
    {
        return JsonSerializer.SerializeToNode(
            new
            {
                name = "Exact blocks",
                definition = new
                {
                    parameters = new[] { new { name = "amount", valueType = "QuoteAmount", defaultValue = number } },
                    values = new[] { new { name = "constant", expression = new { kind = "constant", valueType = "QuoteAmount", number } } },
                    blocks = Array.Empty<object>(), wires = Array.Empty<object>()
                },
                layout = new { positions = new Dictionary<string, object>() }
            })!;
    }
}