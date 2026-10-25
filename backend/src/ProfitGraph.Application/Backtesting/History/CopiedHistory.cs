using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Backtesting.History;

/// <summary>
///     Candles copied out of the cache from <see cref="From" /> on, with the merged market closures between them.
/// </summary>
internal sealed record CopiedHistory(DateTimeOffset From, IReadOnlyList<Candle> Candles, IReadOnlyList<CandleRangeInternal> Closures);