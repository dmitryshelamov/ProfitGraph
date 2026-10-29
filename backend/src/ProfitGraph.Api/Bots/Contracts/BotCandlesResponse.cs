using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Charts;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A page of the bot's chart: closed candles of [From, To) on the bot's timeframe, oldest first, and the bot's fills of
///     the same range. To never passes <see cref="LatestClosed" />; <see cref="ErrorCode" /> says why the venue could not
///     be read when <see cref="Status" /> is <c>Unavailable</c>.
/// </summary>
public sealed record BotCandlesResponse(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset LatestClosed,
    CandleHistoryStatus Status,
    string? ErrorCode,
    IReadOnlyList<CandleResponse> Candles,
    IReadOnlyList<BotChartFillResponse> Fills,
    bool FillsTruncated);