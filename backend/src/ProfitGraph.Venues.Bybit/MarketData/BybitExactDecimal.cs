using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     Checks a Bybit decimal string before the SDK turns it into decimal: the value must fit decimal without rounding. USDT
///     perpetuals quote their smallest prices in exponent notation (<c>9.513e-05</c>, ADR 0061), which counts by its exact value.
/// </summary>
internal static class BybitExactDecimal
{
    private const int MaxScale = 28;
    private static readonly BigInteger MaxCoefficient = (BigInteger.One << 96) - 1;

    public static void Validate(JsonElement element)
    {
        string text = element.GetString() ?? throw new FormatException();
        string[] notation = text.Split('e', 'E');

        if (notation.Length > 2)
        {
            throw new FormatException();
        }

        string[] parts = notation[0].Split('.');

        if (parts.Length > 2 || parts.Any(part => part.Length == 0 || part.Any(character => !char.IsAsciiDigit(character))))
        {
            throw new FormatException();
        }

        int exponent = notation.Length == 2 ? Exponent(notation[1]) : 0;
        string fraction = parts.Length == 2 ? parts[1] : string.Empty;
        BigInteger coefficient = BigInteger.Parse(parts[0] + fraction, CultureInfo.InvariantCulture);
        int scale = fraction.Length - exponent;

        while (scale > 0 && !coefficient.IsZero && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }

        if (scale < 0)
        {
            coefficient *= BigInteger.Pow(value: 10, -scale);
            scale = 0;
        }

        if ((scale > MaxScale && !coefficient.IsZero) || coefficient > MaxCoefficient)
        {
            throw new FormatException();
        }
    }

    private static int Exponent(string text)
    {
        string digits = text.StartsWith('-') || text.StartsWith('+') ? text[1..] : text;

        if (digits.Length is 0 or > 3 || digits.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new FormatException();
        }

        int value = int.Parse(digits, CultureInfo.InvariantCulture);

        return text.StartsWith('-') ? -value : value;
    }
}