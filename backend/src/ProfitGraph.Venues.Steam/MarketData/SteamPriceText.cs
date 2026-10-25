using System.Globalization;
using System.Text.RegularExpressions;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Reads a price Steam writes as localized text, such as <c>615,00₸</c> or <c>$6.15</c> (Profit Fox Designer
///     <c>SteamMarketPriceParser</c>): one numeric span with the currency's affixes, thousands groups of three and at most
///     two fraction digits. The result is in whole minor units; anything else is unreadable rather than guessed.
/// </summary>
internal static partial class SteamPriceText
{
    public static long? MinorUnits(string text, SteamCurrency currency)
    {
        string value = text.Trim();

        if ((currency.Prefix.Length > 0 && !value.StartsWith(currency.Prefix, StringComparison.Ordinal)) ||
            (currency.Suffix.Length > 0 && !value.EndsWith(currency.Suffix, StringComparison.Ordinal)))
        {
            return null;
        }

        Match match = Amount().Match(value);

        if (!match.Success)
        {
            return null;
        }

        string numeric = match.Groups[1].Value;
        int lastSeparator = Math.Max(numeric.LastIndexOf('.'), numeric.LastIndexOf(','));
        bool hasFraction = lastSeparator >= 0 && numeric.Length - lastSeparator - 1 is 1 or 2;
        string whole = hasFraction ? numeric[..lastSeparator] : numeric;
        string[] groups = Separator().Split(whole);
        char[] separators = [.. whole.Where(character => !char.IsAsciiDigit(character)).Select(character => char.IsWhiteSpace(character) ? ' ' : character).Distinct()];

        if (separators.Length > 1 || (hasFraction && separators.Contains(numeric[lastSeparator])) ||
            groups.Any(group => group.Length == 0 || !group.All(char.IsAsciiDigit)) ||
            (groups.Length > 1 && (groups[0].Length > 3 || groups.Skip(1).Any(group => group.Length != 3))))
        {
            return null;
        }

        string normalized = string.Concat(groups) + (hasFraction ? "." + numeric[(lastSeparator + 1)..] : string.Empty);

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0m)
        {
            return null;
        }

        decimal units = amount / currency.MinorUnit;

        return units == decimal.Truncate(units) && units <= long.MaxValue ? (long)units : null;
    }

    [GeneratedRegex(@"^[^0-9+\-]*([0-9][0-9.,\s  ]*[0-9]|[0-9])[^0-9+\-]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Amount();

    [GeneratedRegex(@"[.,\s  ]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Separator();
}