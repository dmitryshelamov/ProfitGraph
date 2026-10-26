using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Reads the display facts of a market search result's <c>asset_description</c>. Steam's text is shown to people, so
///     markup and control characters are stripped and the image is rebuilt from a validated token, never taken as an address.
/// </summary>
internal static class SteamItemAppearanceParser
{
    private const int MaximumLines = 12;
    private const int MaximumLineLength = 300;
    private const int MaximumKindLength = 100;

    private static readonly Regex ImageToken = new Regex("^[A-Za-z0-9_-]{1,1200}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Color = new Regex("^[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static InstrumentAppearanceInternal? Parse(JsonElement result)
    {
        if (!result.TryGetProperty("asset_description", out JsonElement description) || description.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? token = Matching(ImageToken, Text(description, "icon_url_large") ?? Text(description, "icon_url"));
        string? kind = Clean(Text(description, "type"), MaximumKindLength);
        string[] lines = Lines(description);

        return token is null && kind is null && lines.Length == 0
            ? null
            : new InstrumentAppearanceInternal(
                token is null ? null : $"https://community.steamstatic.com/economy/image/{token}/256fx256f",
                kind,
                Matching(Color, Text(description, "name_color")),
                Matching(Color, Text(description, "background_color")),
                lines);
    }

    private static string[] Lines(JsonElement description)
    {
        if (!description.TryGetProperty("descriptions", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. rows.EnumerateArray()
                .Where(row => row.ValueKind == JsonValueKind.Object)
                .Select(row => Clean(Text(row, "value"), MaximumLineLength))
                .OfType<string>()
                .Take(MaximumLines)
        ];
    }

    private static string? Text(JsonElement row, string name)
    {
        return row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? Matching(Regex pattern, string? value)
    {
        return value is not null && pattern.IsMatch(value) ? value : null;
    }

    private static string? Clean(string? value, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        string text = Spaces.Replace(WebUtility.HtmlDecode(Tags.Replace(value, string.Empty)), " ").Trim();

        if (text.Length == 0 || text.Any(char.IsControl))
        {
            return null;
        }

        return text.Length > maximumLength ? text[..maximumLength] : text;
    }
}
