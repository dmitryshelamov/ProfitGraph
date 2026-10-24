using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A venue's live closed candles of one series (ADR 0016). Only confirmed candles arrive, in open-time order without
///     gaps: after <c>afterOpen</c> the source fills a gap from public history before the next live candle. When the feed
///     goes silent the venue module reports it through <c>MarkFeedStaleCommand</c>.
/// </summary>
public interface ILiveCandleSource : IVenuePort
{
    IAsyncEnumerable<Candle> Subscribe(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset? afterOpen, CancellationToken cancellationToken);
}