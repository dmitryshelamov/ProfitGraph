using System.Globalization;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

public sealed class TradingCalculationTests
{
    private static decimal D(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [TestCase(OrderSide.Buy, "100.10")]
    [TestCase(OrderSide.Sell, "99.90")]
    public void Slippage_IsAdverseToTheOrderSide(OrderSide side, string expected)
    {
        SlippageModel.Apply(referencePrice: 100m, side, slippage: 0.001m).ShouldBe(D(expected));
    }

    [TestCase("0.0011", "0.001", 8, "0.0000011")]
    [TestCase("0.000101", "0.001", 8, "0.00000011")]
    [TestCase("99.9", "0.001", 2, "0.10")]
    public void Fee_RoundsUpToPrecision(string gross, string rate, int precision, string expected)
    {
        FeeCalculator.Calculate(D(gross), D(rate), precision).ShouldBe(D(expected));
    }

    [Test]
    public void Fee_IsExactlyZeroForZeroRate_AndNeverExceedsReceivedAmount()
    {
        FeeCalculator.Calculate(grossReceived: 0.123456789m, rate: 0m, precision: 2).ShouldBe(0m);
        FeeCalculator.Calculate(grossReceived: 0.001m, rate: 0.5m, precision: 2).ShouldBe(0.001m);
    }

    [TestCase("1.23456789", "0.001", "1.234")]
    [TestCase("0.0009", "0.001", "0")]
    [TestCase("100.019", "0.01", "100.01")]
    public void Normalization_RoundsDownToStep(string value, string step, string expected)
    {
        DecimalRounding.FloorToStep(D(value), D(step)).ShouldBe(D(expected));
    }
}