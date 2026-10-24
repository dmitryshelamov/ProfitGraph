using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     Checks a Bybit decimal string before the SDK turns it into decimal: the value must fit decimal without rounding.
/// </summary>
internal static class BybitExactDecimal
{
    private static readonly BigInteger MaxCoefficient = (BigInteger.One << 96) - 1;

    public static void Validate(JsonElement element)
    {
        string text = element.GetString() ?? throw new FormatException();
        string[] parts = text.Split('.');

        if (parts.Length > 2 || parts.Any(part => part.Length == 0 || part.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new FormatException();
        }

        string fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;

        if (fraction.Length > 28 || BigInteger.Parse(parts[0] + fraction, CultureInfo.InvariantCulture) > MaxCoefficient)
        {
            throw new FormatException();
        }
    }
}