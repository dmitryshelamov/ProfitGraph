using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Reads the account's buy orders from the signed-in market page (Profit Fox Designer <c>SteamSnapshotParser.BuyOrders</c>):
///     each <c>mybuyorder_&lt;id&gt;</c> row inside <c>myListings</c> and its item link to <c>/market/listings/&lt;app&gt;/&lt;name&gt;</c>.
///     A page without the table, or a row whose item cannot be read, is unreadable, so nothing is decided from part of it.
/// </summary>
internal static partial class SteamAccountBuyOrdersParser
{
    public static IReadOnlyList<SteamAccountBuyOrder>? Read(byte[] html)
    {
        string text = Encoding.UTF8.GetString(html);
        Match container = Container().Match(text);

        if (!container.Success || Content(text, container) is not { } table)
        {
            return null;
        }

        MatchCollection rows = Row().Matches(table);

        if (AnyRow().Matches(table).Count != rows.Count)
        {
            return null;
        }

        var orders = new List<SteamAccountBuyOrder>();

        foreach (Match row in rows)
        {
            if (Content(table, row) is not { } content || Link().Match(content) is not { Success: true } link ||
                Item(WebUtility.HtmlDecode(link.Groups["url"].Value)) is not { } item)
            {
                return null;
            }

            orders.Add(new SteamAccountBuyOrder(row.Groups["id"].Value, item.AppId, item.Name));
        }

        return orders.DistinctBy(order => order.Id).Count() == orders.Count ? orders : null;
    }

    private static (int AppId, string Name)? Item(string url)
    {
        string path = Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute) && absolute.Scheme == Uri.UriSchemeHttps ? absolute.AbsolutePath : url;

        if (!path.StartsWith("/market/listings/", StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = path.Split(separator: '/', count: 5);

        return parts.Length == 5 && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int appId) && appId > 0 &&
            Uri.UnescapeDataString(parts[4]) is { Length: > 0 } name
                ? (appId, name)
                : null;
    }

    private static string? Content(string html, Match opening)
    {
        int start = opening.Index + opening.Length;
        var depth = 1;

        foreach (Match tag in Div().Matches(html[start..]))
        {
            depth += tag.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;

            if (depth == 0)
            {
                return html.Substring(start, tag.Index);
            }
        }

        return null;
    }

    [GeneratedRegex(
        pattern: """<div\b[^>]*\bid\s*=\s*['"]myListings['"][^>]*>""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Container();

    [GeneratedRegex(
        pattern: """<div\b[^>]*\bid\s*=\s*['"]mybuyorder_(?<id>[0-9]{1,64})['"][^>]*>""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Row();

    [GeneratedRegex(pattern: """\bid\s*=\s*['"]mybuyorder_[^'"]*['"]""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AnyRow();

    [GeneratedRegex(
        pattern: """<a\b(?=[^>]*\bclass\s*=\s*['"][^'"]*\bmarket_listing_item_name_link\b[^'"]*['"])[^>]*\bhref\s*=\s*['"](?<url>[^'"]+)['"][^>]*>""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Link();

    [GeneratedRegex(pattern: """</?div\b[^>]*>""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Div();
}