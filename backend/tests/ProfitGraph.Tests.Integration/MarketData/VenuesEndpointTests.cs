using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

public sealed class VenuesEndpointTests
{
    [Test]
    public async Task ListsRegisteredVenuesWithTheirTimeframes()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();

        var venues = (await client.GetFromJsonAsync<JsonArray>("/api/market-data/venues"))!;

        JsonNode venue = venues.Single()!;
        venue["id"]!.GetValue<string>().ShouldBe("bybit");
        venue["name"]!.GetValue<string>().ShouldBe("Bybit");
        venue["timeframes"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldContain("1h");
    }
}