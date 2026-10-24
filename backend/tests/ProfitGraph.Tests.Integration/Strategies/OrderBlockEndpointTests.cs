using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

[NonParallelizable]
public sealed class OrderBlockEndpointTests
{
    private const string ExactAmount = "9007199254740993.123456789";
    private const string ExactQuantity = "0.1234567890123456789012345678";
    private const string ExactPrice = "100.12345678901234567890123456";

    [TestCase("Buy", "amount", "Market")]
    [TestCase("Buy", "quantity", "Market")]
    [TestCase("Sell", "amount", "Market")]
    [TestCase("Sell", "quantity", "Market")]
    [TestCase("Buy", "amount", "Limit")]
    [TestCase("Buy", "quantity", "Limit")]
    [TestCase("Sell", "amount", "Limit")]
    [TestCase("Sell", "quantity", "Limit")]
    public async Task OrderSizeModeIsIndependentOfSideAndBothInputsSurviveSaveLoadAndValidation(string side, string sizeMode, string orderKind)
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        JsonNode created = await Create(client, Request(side, sizeMode, orderKind));
        var id = created["id"]!.GetValue<string>();
        var loaded = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}"))!;
        AssertSettings(loaded, side, sizeMode, orderKind);
        JsonNode.DeepEquals(created["definition"], loaded["definition"]).ShouldBeTrue();
        await AssertValid(client, id);

        string nextSide = side == "Buy" ? "Sell" : "Buy";
        Trade(loaded)["side"] = nextSide;
        using HttpResponseMessage save = await client.PutAsJsonAsync(
            $"/api/strategies/{id}",
            new
            {
                name = "Saved order size",
                expectedVersion = 1,
                definition = loaded["definition"],
                layout = loaded["layout"]
            });
        save.StatusCode.ShouldBe(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        var saved = (await save.Content.ReadFromJsonAsync<JsonNode>())!;
        saved["currentVersion"]!.GetValue<int>().ShouldBe(2);
        AssertSettings(saved, nextSide, sizeMode, orderKind);
        loaded = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}"))!;
        AssertSettings(loaded, nextSide, sizeMode, orderKind);
        JsonNode.DeepEquals(saved["definition"], loaded["definition"]).ShouldBeTrue();
        await AssertValid(client, id);

        var original = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}/versions/1"))!;
        AssertSettings(original, side, sizeMode, orderKind);
        JsonNode.DeepEquals(created["definition"], original["definition"]).ShouldBeTrue();
    }

    [TestCase("Buy", "quantity")]
    [TestCase("Sell", "amount")]
    public async Task MissingInverseMarketConversionPriceIsAVisibleBlockDiagnostic(string side, string sizeMode)
    {
        await using var app = new ProfitGraphApiFactory();
        using HttpClient client = await app.CreateSignedInClient();
        JsonNode created = await Create(client, Request(side, sizeMode, "Market", includePrice: false));
        var id = created["id"]!.GetValue<string>();
        var loaded = (await client.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}"))!;
        Trade(loaded)["sizeMode"]!.GetValue<string>().ShouldBe(sizeMode);
        Trade(loaded)["price"].ShouldBeNull();
        using HttpResponseMessage response = await client.PostAsync($"/api/strategies/{id}/validate", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["isValid"]!.GetValue<bool>().ShouldBeFalse();
        JsonNode diagnostic = validation["diagnostics"]!.AsArray().ShouldHaveSingleItem().ShouldNotBeNull();
        diagnostic["code"]!.GetValue<string>().ShouldBe("MissingInput");
        diagnostic["blockId"]!.GetValue<string>().ShouldBe("trade");
        diagnostic["message"]!.GetValue<string>().ShouldNotContain("$");
        diagnostic.AsObject().ContainsKey("nodeId").ShouldBeFalse();
    }

    private static object Request(string side, string sizeMode, string orderKind, bool includePrice = true)
    {
        return new
        {
            name = "Order size roundtrip",
            definition = new
            {
                parameters = new[] { new { name = "referencePrice", valueType = "Price", defaultValue = ExactPrice } },
                values = new[] { new { name = "mark", expression = new { kind = "parameter", name = "referencePrice" } } },
                blocks = new object[]
                {
                    new { id = "start", kind = "event", source = "OnStart" },
                    new
                    {
                        id = "trade",
                        kind = "order",
                        orderKind,
                        side,
                        sizeMode,
                        amount = new { kind = "constant", valueType = "QuoteAmount", number = ExactAmount },
                        quantity = new { kind = "constant", valueType = "BaseAmount", number = ExactQuantity },
                        price = includePrice ? new { kind = "value", name = "mark" } : null,
                        tag = new { kind = "constant", valueType = "Tag", text = "trade" }
                    }
                },
                wires = new[] { new { from = new { block = "start", exit = "out" }, to = "trade" } }
            },
            layout = new { positions = new Dictionary<string, object> { ["start"] = new { x = 0, y = 0 }, ["trade"] = new { x = 240, y = 0 } } }
        };
    }

    private static async Task<JsonNode> Create(HttpClient client, object request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static JsonNode Trade(JsonNode response)
    {
        return response["definition"]!["blocks"]!.AsArray().Single(block => block!["id"]!.GetValue<string>() == "trade")!;
    }

    private static void AssertSettings(JsonNode response, string side, string sizeMode, string orderKind)
    {
        JsonNode trade = Trade(response);
        trade["side"]!.GetValue<string>().ShouldBe(side);
        trade["sizeMode"]!.GetValue<string>().ShouldBe(sizeMode);
        trade["orderKind"]!.GetValue<string>().ShouldBe(orderKind);
        trade["amount"]!["number"]!.GetValue<string>().ShouldBe(ExactAmount);
        trade["quantity"]!["number"]!.GetValue<string>().ShouldBe(ExactQuantity);
        trade["price"]!["kind"]!.GetValue<string>().ShouldBe("value");
        trade["price"]!["name"]!.GetValue<string>().ShouldBe("mark");
        response["definition"]!["values"]![0]!["expression"]!["name"]!.GetValue<string>().ShouldBe("referencePrice");
        response["definition"]!["parameters"]![0]!["defaultValue"]!.GetValue<string>().ShouldBe(ExactPrice);
    }

    private static async Task AssertValid(HttpClient client, string id)
    {
        using HttpResponseMessage response = await client.PostAsync($"/api/strategies/{id}/validate", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        validation["isValid"]!.GetValue<bool>().ShouldBeTrue(validation.ToJsonString());
        validation["diagnostics"]!.AsArray().ShouldBeEmpty();
    }
}