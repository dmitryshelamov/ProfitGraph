using System.Text.RegularExpressions;

using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     Builds the display card of a share. The logo is rebuilt from a validated file name on T-Invest's brand CDN, never
///     taken as an address. The Invest API's share carries no brand text, so the description is the sector and the country
///     of risk it does report.
/// </summary>
internal static class TInvestShareAppearance
{
    private static readonly Regex LogoFile = new Regex(@"^([A-Za-z0-9_-]{1,40})\.png$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Color = new Regex("^#?([0-9A-Fa-f]{6})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Unsafe = new Regex(@"[^\p{L}\p{N} _.,()/-]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static InstrumentAppearanceInternal? From(TInvestShare share)
    {
        Match logo = LogoFile.Match(share.Brand?.LogoName ?? string.Empty);
        Match color = Color.Match(share.Brand?.LogoBaseColor ?? string.Empty);
        var lines = new List<string>();

        AddLine(lines, "Сектор", share.Sector);
        AddLine(lines, "Страна риска", share.Country);

        return new InstrumentAppearanceInternal(
            logo.Success ? $"https://invest-brands.cdn-tinkoff.ru/{logo.Groups[1].Value}x160.png" : null,
            share.Name.Length == 0 ? null : share.Name,
            NameColor: null,
            color.Success ? color.Groups[1].Value : null,
            lines);
    }

    private static void AddLine(List<string> lines, string label, string value)
    {
        string text = Unsafe.Replace(value, string.Empty).Trim();

        if (text.Length is > 0 and <= 100)
        {
            lines.Add($"{label}: {text}");
        }
    }
}