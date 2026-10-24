using System.Globalization;

namespace ProfitGraph.Application.Common.Numbers;

/// <summary>
///     Canonical invariant decimal text without trailing zeros: 100, 100.0 and 100.00 give the same string.
///     Use it wherever a string takes part in a hash, a cursor or a contract.
/// </summary>
public static class ExactDecimalText
{
    private const string CanonicalFormat = "0.############################";

    public static string Format(decimal value)
    {
        return value.ToString(CanonicalFormat, CultureInfo.InvariantCulture);
    }
}