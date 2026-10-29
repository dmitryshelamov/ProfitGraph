using System.Text.Json;

using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     A Bybit decimal string must fit decimal without rounding, in plain or exponent notation (ADR 0061).
/// </summary>
public sealed class BybitExactDecimalTests
{
    [TestCase("100.1234567890123456789")]
    [TestCase("9.513e-05")]
    [TestCase("9.533E-5")]
    [TestCase("1.5e+3")]
    [TestCase("0.0000000000000000000000000001")]
    [TestCase("1e-28")]
    [TestCase("0e-40")]
    public void AcceptsWhatDecimalHoldsExactly(string text)
    {
        Should.NotThrow(() => BybitExactDecimal.Validate(Element(text)));
    }

    [TestCase("1e-29")]
    [TestCase("1e29")]
    [TestCase("1.5e")]
    [TestCase("1e5e3")]
    [TestCase("-1")]
    [TestCase("1.2.3")]
    [TestCase("e5")]
    public void RefusesWhatDecimalCannotHoldOrIsNoNumber(string text)
    {
        Should.Throw<FormatException>(() => BybitExactDecimal.Validate(Element(text)));
    }

    private static JsonElement Element(string text)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(text));

        return document.RootElement.Clone();
    }
}