using ProfitGraph.Application.Jobs;

namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     What a download covered: the resolved range, candles found in the cache, fetched from the venue and newly stored.
/// </summary>
public sealed record HistoricalDownloadInternal(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    long Total,
    long Cached,
    long Downloaded,
    long Inserted) : IBackgroundJobResult;