using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Charts;

/// <summary>
///     The closed candles of the half-open UTC range [From, To) of a chart, oldest first. To never passes
///     <see cref="LatestClosed" />, the end of the latest closed candle when the page was read; <see cref="ErrorCode" /> says why
///     the venue could not be read.
/// </summary>
public sealed record CandleChartPage(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset LatestClosed,
    CandleHistoryStatus Status,
    string? ErrorCode,
    IReadOnlyList<Candle> Candles);