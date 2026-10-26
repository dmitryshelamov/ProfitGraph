using ProfitGraph.Application.Bots.GetBotCandles;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     One page of the bot's chart: the closed candles of the half-open UTC range [From, To) on the bot's timeframe, oldest
///     first, and the bot's fills of the same range. <see cref="LatestClosed" /> is the end of the latest closed candle when
///     the page was read; the page never reaches past it. <see cref="ErrorCode" /> says why the venue could not be read.
/// </summary>
public sealed record BotCandlesInternal(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset LatestClosed,
    BotCandleHistoryStatus Status,
    string? ErrorCode,
    IReadOnlyList<CandleInternal> Candles,
    IReadOnlyList<BotChartFillInternal> Fills,
    bool FillsTruncated);