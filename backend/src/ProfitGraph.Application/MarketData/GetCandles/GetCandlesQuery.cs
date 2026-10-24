using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.GetCandles;

/// <summary>
///     Reads at most <paramref name="Limit" /> stored candles of the half-open UTC range [From, To), oldest first.
/// </summary>
public sealed record GetCandlesQuery(string Venue, string Symbol, string Timeframe, DateTimeOffset From, DateTimeOffset To, int Limit)
    : IQuery<IReadOnlyList<CandleInternal>>;