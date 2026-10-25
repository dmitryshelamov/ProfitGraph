using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Unit.Domain.MarketData;

public sealed class InstrumentSymbolTests
{
    [TestCase("BTCUSDT")]
    [TestCase("6f1c2c55-7c1b-4d43-9a43-6d2f8f6b1a01")]
    [TestCase("KZT:730:Dreams & Nightmares Case")]
    [TestCase("USD:480:Example/blue: Mk II")]
    [TestCase("EUR:730:StatTrak™ ★ Karambit | Doppler (Factory New)")]
    public void KeepsTheVenueFormAsIs(string value)
    {
        new InstrumentSymbol(value).Value.ShouldBe(value);
    }

    [TestCase("")]
    [TestCase(" BTCUSDT")]
    [TestCase("BTCUSDT ")]
    [TestCase("BTC  USDT")]
    [TestCase("BTC\tUSDT")]
    [TestCase("BTC\nUSDT")]
    [TestCase("BTC USDT")]
    [TestCase("BTC\u0000USDT")]
    public void RejectsEmptySymbolsAndAnyOtherWhitespace(string value)
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