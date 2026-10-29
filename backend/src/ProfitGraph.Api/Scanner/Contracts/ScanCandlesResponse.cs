using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Charts;

namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A page of a scanner pair's chart: closed candles of [From, To), oldest first. To never passes <see cref="LatestClosed" />;
///     <see cref="ErrorCode" /> says why the venue could not be read when <see cref="Status" /> is <c>Unavailable</c>.
/// </summary>
public sealed record ScanCandlesResponse(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset LatestClosed,
    CandleHistoryStatus Status,
    string? ErrorCode,
    IReadOnlyList<CandleResponse> Candles);