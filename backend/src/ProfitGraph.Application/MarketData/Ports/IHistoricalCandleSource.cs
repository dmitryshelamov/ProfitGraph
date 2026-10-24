using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     A venue's public closed-candle history (ADR 0003: a capability is a port). Failures are
///     <see cref="HistoricalDataException" /> with a stable code.
/// </summary>
public interface IHistoricalCandleSource
{
    VenueId Venue { get; }

    /// <summary>
    ///     The source recorded with downloaded candles.
    /// </summary>
    string Source { get; }

    /// <summary>
    ///     Finds the open time of the venue's first candle before <paramref name="to" />, or null when there is none.
    /// </summary>
    Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the venue's candles of [from, to), oldest first.
    /// </summary>
    Task<IReadOnlyList<Candle>> Get(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}