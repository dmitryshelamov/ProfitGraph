using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     A venue's public closed-candle history (ADR 0003: a capability is a port). Failures are
///     <see cref="HistoricalDataException" /> with a stable code.
/// </summary>
public interface IHistoricalCandleSource : IVenuePort
{
    /// <summary>
    ///     The source recorded with downloaded candles.
    /// </summary>
    string Source { get; }

    /// <summary>
    ///     Whether the venue recomputes a closed candle after the fact, as a venue whose candles are medians of sales does (ADR 0029). A live bot
    ///     that restarts continues on the restated candle; on a venue whose closed candles are final, a different candle is a
    ///     data correction that stops the bot.
    /// </summary>
    bool RestatesClosedCandles => false;

    /// <summary>
    ///     Finds the open time of the venue's first candle before <paramref name="to" />, or null when there is none.
    /// </summary>
    Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the venue's candles of [from, to), oldest first, with the ranges of it in which the market was closed.
    /// </summary>
    Task<HistoricalCandlesInternal> Get(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}