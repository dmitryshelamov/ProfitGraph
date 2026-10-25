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

        JsonNode venue = venues.Single(item => item!["id"]!.GetValue<string>() == "bybit")!;
        venue["id"]!.GetValue<string>().ShouldBe("bybit");
        venue["name"]!.GetValue<string>().ShouldBe("Bybit");
        venue["timeframes"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldContain("1h");
        venue["executionDefaults"].ShouldBeNull();
        JsonNode steam = venues.Single(item => item!["id"]!.GetValue<string>() == "steam")!;
        steam["timeframes"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["1h", "1d"]);
        steam["executionDefaults"]!["sellerFees"]!["minimum"]!.GetValue<string>().ShouldBe("0.01");
        steam["executionDefaults"]!["settlementHoldSeconds"]!.GetValue<long>().ShouldBe(604_800);
        venue["botSettings"].ShouldBeNull();
        steam["botSettings"]!["settlementHoldDays"]!.GetValue<int>().ShouldBe(7);
        steam["botSettings"]!["marketBuySlippage"]!.GetValue<string>().ShouldBe("0");
        steam["botSettings"]!["marketBuyDeadlineSeconds"]!.GetValue<int>().ShouldBe(120);
    }

    [Test]
    public async Task ShowsAVenuesExecutionDefaults()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();

        var venues = (await client.GetFromJsonAsync<JsonArray>("/api/market-data/venues"))!;

        JsonNode fees = venues.Single(item => item!["id"]!.GetValue<string>() == TestHistoryVenue.WithHistory)!["executionDefaults"]!["sellerFees"]!;
        fees["rates"]!.AsArray().Select(rate => rate!.GetValue<string>()).ShouldBe(["0.05", "0.1"]);
        fees["minimum"]!.GetValue<string>().ShouldBe("0.01");
    }
}