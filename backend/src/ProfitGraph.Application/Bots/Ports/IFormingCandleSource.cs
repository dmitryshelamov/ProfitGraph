using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A venue's live updates of the candle being formed in one series (ADR 0026), separate from <see cref="ILiveCandleSource" />,
///     which delivers closed candles to bots. Updates arrive as the venue sends them, a venue that marks closing says so, and
///     the stream throws or ends when the connection is lost; the caller connects again. A venue that reads market data
///     with the caller's credential finds it in <c>MarketDataAccess.Current</c>. Nothing is stored.
/// </summary>
public interface IFormingCandleSource : IVenuePort
{
    /// <summary>
    ///     Whether the venue streams candles of this timeframe.
    /// </summary>
    bool Supports(Timeframe timeframe);

    IAsyncEnumerable<FormingCandleInternal> Subscribe(InstrumentSymbol symbol, Timeframe timeframe, CancellationToken cancellationToken);
}