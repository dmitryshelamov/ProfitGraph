using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Unit.Domain.MarketData;

public sealed class InstrumentSymbolTests
{
    [TestCase("BTCUSDT")]
    [TestCase("6f1c2c55-7c1b-4d43-9a43-6d2f8f6b1a01")]
    public void KeepsTheVenueFormAsIs(string value)
    {
        new InstrumentSymbol(value).Value.ShouldBe(value);
    }

    [TestCase("")]
    [TestCase("BTC USDT")]
    [TestCase("BTC\tUSDT")]
    public void RejectsEmptyOrSpacedSymbols(string value)
    {
        Should.Throw<ArgumentException>(() => new InstrumentSymbol(value));
    }

    [Test]
    public void LimitsTheLength()
    {
        new InstrumentSymbol(new string(c: 'A', InstrumentSymbol.MaxLength)).Value.Length.ShouldBe(InstrumentSymbol.MaxLength);
        Should.Throw<ArgumentException>(() => new InstrumentSymbol(new string(c: 'A', InstrumentSymbol.MaxLength + 1)));
    }
}