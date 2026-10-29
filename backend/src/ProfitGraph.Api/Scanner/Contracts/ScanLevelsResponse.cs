using ProfitGraph.Application.MarketData.Charts;

namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A scanner pair's levels on one timeframe as exact decimal strings: every level of the closed candles of [From, To), lowest
///     first, and at <see cref="Price" /> the nearest support and resistance, their distances in percent and the reward-to-risk of a
///     long. An empty value is null; <see cref="ErrorCode" /> says why the venue could not be read.
/// </summary>
public sealed record ScanLevelsResponse(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    CandleHistoryStatus Status,
    string? ErrorCode,
    int CandleCount,
    string? Price,
    IReadOnlyList<string> Levels,
    string? Support,
    string? Resistance,
    string? DistanceToSupport,
    string? DistanceToResistance,
    string? RewardRisk);