using System.Net;
using System.Text;

using Microsoft.Extensions.Options;

using ProfitGraph.Venues.Steam.Inventory;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Inventory pages with invented assets in the layout Profit Fox Designer read from real accounts.
/// </summary>
public sealed class SteamInventoryTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void ReadsTheContextsAnAppAdvertises()
    {
        var html = """<script>var g_rgAppContextData = {"730":{"appid":730,"rgContexts":{"2":{"id":"2"},"16":{"id":"16"}}},"570":{"rgContexts":{"2":{}}}};</script>""";

        SteamInventoryParser.Contexts(Encoding.UTF8.GetBytes(html), appId: 730).ShouldBe(["16", "2"]);
        SteamInventoryParser.Contexts(Encoding.UTF8.GetBytes(html), appId: 440).ShouldBeEmpty();
    }

    [Test]
    public void ReadsAssetsWithTheirUnlockTimes()
    {
        SteamInventoryPage page = SteamInventoryParser.Page(Encoding.UTF8.GetBytes(Page("2", more: true)), appId: 730, "2", Now);

        page.NextAssetId.ShouldBe("1002");
        page.Assets.Select(asset => (asset.AssetId, asset.MarketHashName, asset.Marketable, asset.UnlockAt))
            .ShouldBe(
            [
                ("1001", "Dreams & Nightmares Case", true, Now),
                ("1002", "Dreams & Nightmares Case", false, new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 7, minute: 0, second: 0, TimeSpan.Zero))
            ]);
        page.Assets[0].SellableAt(Now).ShouldBeTrue();
        page.Assets[1].SellableAt(Now).ShouldBeFalse();
    }

    [Test]
    public void AnEmptyContextIsOnlyEmptyWhenSteamSaysSo()
    {
        SteamInventoryParser.Page(Encoding.UTF8.GetBytes("""{"success":1,"total_inventory_count":0}"""), appId: 730, "2", Now).Assets.ShouldBeEmpty();
        Should.Throw<SteamInventoryException>(() => SteamInventoryParser.Page(Encoding.UTF8.GetBytes("""{"success":1}"""), appId: 730, "2", Now));
    }

    [TestCase("""{"success":1,"assets":[{"appid":730,"contextid":"2","assetid":"1","classid":"7","instanceid":"0","amount":"1"}],"descriptions":[]}""")]
    [TestCase(
        """{"success":1,"assets":[{"appid":730,"contextid":"9","assetid":"1","classid":"7","instanceid":"0","amount":"1"}],"descriptions":[{"appid":730,"classid":"7","instanceid":"0","market_hash_name":"X","marketable":1}]}""")]
    [TestCase("""{"success":0}""")]
    public void AnythingIncompleteRefusesTheWholePage(string json)
    {
        Should.Throw<SteamInventoryException>(() => SteamInventoryParser.Page(Encoding.UTF8.GetBytes(json), appId: 730, "2", Now));
    }

    [Test]
    public void AnUnreadableUnlockLeavesTheItemLocked()
    {
        string json = Page("2", more: false).Replace("1 Nov @ 7:00am", "sometime soon", StringComparison.Ordinal);

        SteamInventoryParser.Page(Encoding.UTF8.GetBytes(json), appId: 730, "2", Now).Assets[1].UnlockAt.ShouldBeNull();
    }

    [Test]
    public async Task TheReaderWalksEveryContextAndPage()
    {
        var harness = new SteamSessionHarness();
        harness.Steam.Override = request =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path == $"/profiles/{MockSteam.SteamId}/inventory/")
            {
                return Answer(body: """<script>var g_rgAppContextData = {"730":{"rgContexts":{"2":{},"16":{}}}};</script>""", "text/html");
            }

            if (path == $"/inventory/{MockSteam.SteamId}/730/2")
            {
                return Answer(
                    request.RequestUri.Query.Contains("start_assetid=1002", StringComparison.Ordinal) ? Page("2", more: false, first: 1003) : Page("2", more: true));
            }

            return path == $"/inventory/{MockSteam.SteamId}/730/16" ? Answer("""{"success":1,"total_inventory_count":0}""") : null;
        };

        IReadOnlyList<SteamInventoryAsset> assets = await new SteamInventoryReader(Options.Create(harness.Options), harness.Clock)
            .Read(harness.Session(), appId: 730, CancellationToken.None);

        assets.Select(asset => asset.AssetId).ShouldBe(["1001", "1002", "1003", "1004"]);
    }

    [Test]
    public async Task ThePageSteamSendsThroughTheEligibilityCheckAndBackIsStillRead()
    {
        var harness = new SteamSessionHarness();
        var passedCheck = false;
        var inventory = $"/profiles/{MockSteam.SteamId}/inventory/";
        harness.Steam.Override = request =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path == inventory && !passedCheck)
            {
                return Redirect($"/market/eligibilitycheck/?goto={Uri.EscapeDataString(inventory)}");
            }

            if (path == "/market/eligibilitycheck/")
            {
                passedCheck = true;

                return Redirect(inventory);
            }

            if (path == inventory)
            {
                return Answer(body: """<script>var g_rgAppContextData = {"730":{"rgContexts":{"2":{}}}};</script>""", "text/html");
            }

            return path == $"/inventory/{MockSteam.SteamId}/730/2" ? Answer(Page("2", more: false)) : null;
        };

        IReadOnlyList<SteamInventoryAsset> assets = await new SteamInventoryReader(Options.Create(harness.Options), harness.Clock)
            .Read(harness.Session(), appId: 730, CancellationToken.None);

        assets.Select(asset => asset.AssetId).ShouldBe(["1001", "1002"]);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.Relative);

        return response;
    }

    private static HttpResponseMessage Answer(string body, string mediaType = "application/json")
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
    }

    private static string Page(string context, bool more, int first = 1001)
    {
        return $$$"""
                  {"success":1,"total_inventory_count":4,{{{(more ? $"\"more_items\":1,\"last_assetid\":\"{first + 1}\"," : "")}}}
                   "assets":[{"appid":730,"contextid":"{{{context}}}","assetid":"{{{first}}}","classid":"10","instanceid":"0","amount":"1"},
                             {"appid":730,"contextid":"{{{context}}}","assetid":"{{{first + 1}}}","classid":"11","instanceid":"0","amount":"1"}],
                   "descriptions":[{"appid":730,"classid":"10","instanceid":"0","market_hash_name":"Dreams & Nightmares Case","marketable":1,"tradable":1},
                                   {"appid":730,"classid":"11","instanceid":"0","market_hash_name":"Dreams & Nightmares Case","marketable":0,"tradable":0,
                                    "owner_descriptions":[{"type":"html","value":"Tradable/Marketable After 1 Nov @ 7:00am"}]}]}
                  """;
    }
}