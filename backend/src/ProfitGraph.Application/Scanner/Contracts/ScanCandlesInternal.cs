using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     One page of a scanner pair's chart: the closed candles of the half-open UTC range [From, To), oldest first. The page never
///     reaches past <see cref="LatestClosed" />; <see cref="ErrorCode" /> says why the venue could not be read.
/// </summary>
public sealed record ScanCandlesInternal(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset LatestClosed,
    CandleHistoryStatus Status,
    string? ErrorCode,
    IReadOnlyList<CandleInternal> Candles);