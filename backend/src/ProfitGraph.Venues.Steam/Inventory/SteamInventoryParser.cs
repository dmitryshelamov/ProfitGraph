using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ProfitGraph.Venues.Steam.Inventory;

/// <summary>
///     Reads an account's inventory as Profit Fox Designer did after its live fixes: the contexts an app advertises in
///     <c>g_rgAppContextData</c> on the profile's inventory page, and pages of <c>/inventory/{steamId}/{appId}/{context}</c>.
///     Every asset must match exactly one description. An unlock time comes only from a single
///     <c>Tradable/Marketable After d MMM @ h:mmtt</c> line, whose year is the one that puts it within a day before and 30
///     days after now; anything unreadable leaves the item locked (Profit Fox Designer ADR 0081).
/// </summary>
internal static class SteamInventoryParser
{
    private static readonly string[] UnlockFormats = ["d MMM '@' h:mmtt yyyy", "dd MMM '@' h:mmtt yyyy"];

    public static IReadOnlyList<string> Contexts(byte[] html, int appId)
    {
        string text = Encoding.UTF8.GetString(html);
        int name = text.IndexOf("g_rgAppContextData", StringComparison.Ordinal);
        int start = name < 0 ? -1 : text.IndexOf(value: '{', name);

        if (start < 0)
        {
            throw Invalid();
        }

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text[start..]));
            using JsonDocument document = JsonDocument.ParseValue(ref reader);

            if (!document.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out JsonElement app))
            {
                return [];
            }

            return app.TryGetProperty("rgContexts", out JsonElement contexts) && contexts.ValueKind == JsonValueKind.Object
                ? [.. contexts.EnumerateObject().Select(context => context.Name).Where(context => context.All(char.IsAsciiDigit)).Order(StringComparer.Ordinal)]
                : throw Invalid();
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    public static SteamInventoryPage Page(byte[] body, int appId, string context, DateTimeOffset now)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (!Flag(root, "success"))
            {
                throw Invalid();
            }

            // An empty context has no arrays at all; only an explicit zero total proves it is empty.
            if (!root.TryGetProperty("assets", out JsonElement rows) && !root.TryGetProperty("descriptions", out _))
            {
                return Text(root, "total_inventory_count") == "0" ? new SteamInventoryPage([], NextAssetId: null) : throw Invalid();
            }

            if (rows.ValueKind != JsonValueKind.Array || !root.TryGetProperty("descriptions", out JsonElement descriptions) ||
                descriptions.ValueKind != JsonValueKind.Array)
            {
                throw Invalid();
            }

            var app = appId.ToString(CultureInfo.InvariantCulture);
            var assets = new List<SteamInventoryAsset>();

            foreach (JsonElement row in rows.EnumerateArray())
            {
                JsonElement[] matches =
                [
                    .. descriptions.EnumerateArray()
                        .Where(description => Text(description, "appid") == app && Text(description, "classid") == Text(row, "classid") &&
                            Text(description, "instanceid") == Text(row, "instanceid"))
                ];

                if (Text(row, "appid") != app || Text(row, "contextid") != context || Text(row, "assetid") is not { Length: > 0 } assetId ||
                    !int.TryParse(Text(row, "amount"), NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount < 1 ||
                    matches.Length != 1 || Text(matches[0], "market_hash_name") is not { Length: > 0 } name)
                {
                    throw Invalid();
                }

                bool marketable = Flag(matches[0], "marketable");
                assets.Add(new SteamInventoryAsset(appId, context, assetId, name, amount, marketable, Unlock(matches[0], marketable, now)));
            }

            if (assets.DistinctBy(asset => asset.AssetId).Count() != assets.Count)
            {
                throw Invalid();
            }

            string? next = Flag(root, "more_items") ? Text(root, "last_assetid") ?? throw Invalid() : null;

            return new SteamInventoryPage(assets, next);
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    private static DateTimeOffset? Unlock(JsonElement description, bool marketable, DateTimeOffset now)
    {
        string[] lines = description.TryGetProperty("owner_descriptions", out JsonElement owner) && owner.ValueKind == JsonValueKind.Array
            ? Lines(owner)
            : [];

        if (description.TryGetProperty("descriptions", out JsonElement general) && general.ValueKind == JsonValueKind.Array)
        {
            lines = [.. lines, .. Lines(general)];
        }

        if (lines.Length == 0)
        {
            return marketable ? now : null;
        }

        if (lines.Length != 1)
        {
            return null;
        }

        string text = lines[0][(lines[0].IndexOf("After ", StringComparison.Ordinal) + 6)..].Trim();
        DateTimeOffset[] candidates =
        [
            .. Enumerable.Range(now.Year - 1, count: 3)
                .Select(year => DateTimeOffset.TryParseExact(
                    text + " " + year.ToString(CultureInfo.InvariantCulture),
                    UnlockFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset candidate)
                    ? candidate
                    : (DateTimeOffset?)null)
                .OfType<DateTimeOffset>()
                .Where(candidate => candidate >= now.AddDays(-1) && candidate <= now.AddDays(30))
        ];

        return candidates.Length == 1 && (marketable || candidates[0] > now) ? candidates[0] : null;
    }

    private static string[] Lines(JsonElement rows)
    {
        return
        [
            .. rows.EnumerateArray()
                .Select(row => Text(row, "value") ?? string.Empty)
                .Where(value => value.StartsWith("Tradable/Marketable After ", StringComparison.Ordinal) ||
                    value.StartsWith("Marketable After ", StringComparison.Ordinal))
        ];
    }

    private static string? Text(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
    }

    private static bool Flag(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out JsonElement value) &&
            (value.ValueKind == JsonValueKind.True || (value.ValueKind is JsonValueKind.Number or JsonValueKind.String && value.ToString() == "1"));
    }

    private static SteamInventoryException Invalid()
    {
        return new SteamInventoryException("The Steam inventory is unreadable or incomplete.");
    }
}