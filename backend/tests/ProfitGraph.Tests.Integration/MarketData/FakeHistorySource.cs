using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     The test venue's history: complete candles from <see cref="First" /> on unless a test replaces them, with every
///     requested range recorded.
/// </summary>
public sealed class FakeHistorySource : IHistoricalCandleSource
{
    public static readonly DateTimeOffset DefaultFirst = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    public DateTimeOffset? First { get; set; } = DefaultFirst;

    public Func<Timeframe, DateTimeOffset, DateTimeOffset, IReadOnlyList<Candle>>? Candles { get; set; }

    /// <summary>
    ///     The market closures reported with each requested range; none unless a test sets them.
    /// </summary>
    public Func<Timeframe, DateTimeOffset, DateTimeOffset, IReadOnlyList<CandleRangeInternal>>? Closures { get; set; }

    /// <summary>
    ///     Runs before each history request with the request's token, for example to hold the request until it is cancelled.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeGet { get; set; }

    public List<(DateTimeOffset From, DateTimeOffset To)> Requests { get; } = [];

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public string Source => "test";

    public Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken)
    {
        return Task.FromResult(First < to ? First : null);
    }

    public async Task<HistoricalCandlesInternal> Get(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add((from, to));
        }

        if (BeforeGet is not null)
        {
            await BeforeGet(cancellationToken);
        }

        return new HistoricalCandlesInternal(Candles?.Invoke(timeframe, from, to) ?? Complete(timeframe, from, to), Closures?.Invoke(timeframe, from, to) ?? []);
    }

    public void Reset()
    {
        First = DefaultFirst;
        Candles = null;
        Closures = null;
        BeforeGet = null;

        lock (Requests)
        {
            Requests.Clear();
        }
    }

    public static IReadOnlyList<Candle> Complete(Timeframe timeframe, DateTimeOffset from, DateTimeOffset to)
    {
        var candles = new List<Candle>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            candles.Add(new Candle(time, Open: 100, High: 110, Low: 90, Close: 105, Volume: 1));
        }

        return candles;
    }
}