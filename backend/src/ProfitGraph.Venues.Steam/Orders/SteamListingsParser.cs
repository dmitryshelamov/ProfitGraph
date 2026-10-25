using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Reads a page of <c>/market/mylistings/</c> (Profit Fox Designer <c>SteamSnapshotParser.Listings</c>): each row's
///     <c>RemoveMarketListing</c> or <c>CancelMarketListingConfirmation</c> control names the listing ID, app, context and
///     asset. A row marker without its control, a repeated ID or unreadable totals make the page unreadable.
/// </summary>
internal static partial class SteamListingsParser
{
    public static SteamListingsPage? Page(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || (root.TryGetProperty("success", out _) && SteamWriteReplyParser.Integer(root, "success") != 1) ||
                !root.TryGetProperty("results_html", out JsonElement html) || html.ValueKind != JsonValueKind.String ||
                SteamWriteReplyParser.Integer(root, "total_count") is not ({ } total and >= 0) ||
                SteamWriteReplyParser.Integer(root, "pagesize") is not ({ } pageSize and > 0 and <= 100))
            {
                return null;
            }

            string text = WebUtility.HtmlDecode(html.GetString()!);
            var listings = new List<SteamListing>();

            foreach (Match control in Control().Matches(text))
            {
                if (!int.TryParse(control.Groups["app"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int appId) || appId <= 0)
                {
                    return null;
                }

                listings.Add(
                    new SteamListing(
                        control.Groups["id"].Value,
                        appId,
                        control.Groups["context"].Value,
                        control.Groups["asset"].Value,
                        control.Groups["kind"].Value == "CancelMarketListingConfirmation"));
            }

            // Row helpers such as mylisting_<id>_image belong to their row; only a bare numeric ID marks a listing.
            bool everyRowRead = Marker().Matches(text).All(marker => listings.Any(listing => listing.Id == marker.Groups["id"].Value));

            return everyRowRead && listings.DistinctBy(listing => listing.Id).Count() == listings.Count
                ? new SteamListingsPage(listings, total, pageSize)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(
        pattern:
        """\b(?<kind>RemoveMarketListing|CancelMarketListingConfirmation)\s*\(\s*['"]mylisting['"]\s*,\s*['"](?<id>[0-9]{1,64})['"]\s*,\s*['"]?(?<app>[0-9]{1,9})['"]?\s*,\s*['"](?<context>[0-9]{1,20})['"]\s*,\s*['"](?<asset>[0-9]{1,32})['"]\s*\)""",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex Control();

    [GeneratedRegex(pattern: """\bid\s*=\s*['"]mylisting_(?<id>[0-9]+)['"]""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Marker();
}