using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Application.Backtesting;

public sealed class HistoryIdentityCalculatorTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly CandleSeries Series = new CandleSeries(new VenueId("bybit"), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h"));

    private static readonly Candle[] Candles =
    [
        new Candle(Start, Open: 100m, High: 110m, Low: 90m, Close: 105m, Volume: 1.5m),
        new Candle(Start.AddHours(1), Open: 105m, High: 106m, Low: 104m, Close: 104.5m, Volume: 0m)
    ];

    /// <summary>
    ///     Pinned so that a change of the identity, which separates otherwise equal runs, is always deliberate.
    /// </summary>
    [Test]
    public void IdentityOfFixedHistoryIsStable()
    {
        Calculate(Range()).ShouldBe("a79392a9b5b552eaa4b46b230684bff45ff9b44f019891ef7e89969608629899");
    }

    [Test]
    public void ClosuresChangeTheIdentity_AndNoClosuresKeepItsBytes()
    {
        string identity = Calculate(Range(), closures: []);
        Candle[] apart = [Candles[0], Candles[1] with { OpenTime = Start.AddHours(3) }];

        identity.ShouldBe("a79392a9b5b552eaa4b46b230684bff45ff9b44f019891ef7e89969608629899");
        Calculate(Range(), apart, closures: [new MarketClosure(Start.AddHours(1), Start.AddHours(3))])
            .ShouldNotBe(Calculate(Range(), apart, closures: [new MarketClosure(Start.AddHours(1), Start.AddHours(2))]));
        Calculate(Range(), apart, closures: [new MarketClosure(Start.AddHours(1), Start.AddHours(3))]).ShouldNotBe(Calculate(Range(), apart));
    }

    [Test]
    public void ACandleWithoutTradesChangesTheIdentity()
    {
        Candle flat = Candles[1] with { Open = 104.5m, High = 104.5m, Low = 104.5m };

        Calculate(Range(), [Candles[0], flat with { NoTrades = true }]).ShouldNotBe(Calculate(Range(), [Candles[0], flat]));
    }

    [Test]
    public void ObservationTimeIsLeftOut_ButTheLatestClosedBoundaryCounts()
    {
        string identity = Calculate(Range());

        Calculate(Range() with { ObservedAt = Start.AddDays(1).AddMinutes(30) }).ShouldBe(identity);
        Calculate(Range() with { LatestClosed = Start.AddDays(2) }).ShouldNotBe(identity);
    }

    [Test]
    public void DecimalScaleIsLeftOut_ButEveryValueCounts()
    {
        string identity = Calculate(Range());

        Calculate(Range(), [Candles[0] with { Close = 105.000m }, Candles[1]]).ShouldBe(identity);
        Calculate(Range(), [Candles[0] with { Volume = 1.6m }, Candles[1]]).ShouldNotBe(identity);
        Calculate(Range() with { Series = new CandleSeries(new VenueId("other"), Series.Symbol, Series.Timeframe) }).ShouldNotBe(identity);
        Calculate(Range(), warmupCount: 0).ShouldNotBe(identity);
    }

    private static ResolvedCandleRange Range()
    {
        return new ResolvedCandleRange(Series, Start.AddHours(1), Start.AddHours(2), Start.AddDays(1), Start.AddDays(1));
    }

    private static string Calculate(ResolvedCandleRange range, IReadOnlyList<Candle>? candles = null, int warmupCount = 1, IReadOnlyList<MarketClosure>? closures = null)
    {
        return HistoryIdentityCalculator.Calculate(range, Start, warmupCount, candles ?? Candles, closures ?? []);
    }
}