using System.Globalization;

using ProfitGraph.Application.Common.Numbers;

namespace ProfitGraph.Api.Common.Parsing;

/// <summary>
///     Parses invariant numeric strings without silent decimal rounding or floating point.
/// </summary>
internal static class ExactGraphNumberParser
{
    public static string Format(decimal value)
    {
        return ExactDecimalText.Format(value);
    }

    public static bool TryDecimal(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string unsigned = text[0] == '-' ? text[1..] : text;
        string[] parts = unsigned.Split('.');

        if (parts.Length is < 1 or > 2 || parts.Any(part => part.Length == 0 || part.Any(character => character is < '0' or > '9'))
            || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        string integer = parts[0].TrimStart('0');
        string fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        string canonical = (integer.Length == 0 ? "0" : integer) + (fraction.Length == 0 ? string.Empty : "." + fraction);

        if (text[0] == '-' && canonical != "0")
        {
            canonical = "-" + canonical;
        }

        return Format(value) == canonical;
    }
}