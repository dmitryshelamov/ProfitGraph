using ProfitGraph.Application.MarketData.Charts;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A scanner pair's levels on one timeframe (ADR 0059, section 8): every level of the closed candles of [From, To), lowest first,
///     and at <see cref="Price" /> the nearest support and resistance, their distances in percent and the reward-to-risk of a long.
///     A value is empty without what it needs; <see cref="ErrorCode" /> says why the venue could not be read.
/// </summary>
public sealed record ScanLevelsInternal(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    CandleHistoryStatus Status,
    string? ErrorCode,
    int CandleCount,
    decimal? Price,
    IReadOnlyList<decimal> Levels,
    decimal? Support,
    decimal? Resistance,
    decimal? DistanceToSupport,
    decimal? DistanceToResistance,
    decimal? RewardRisk);