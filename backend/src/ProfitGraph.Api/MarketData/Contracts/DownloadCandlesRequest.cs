namespace ProfitGraph.Api.MarketData.Contracts;

/// <summary>
///     A history download: omitted bounds mean the venue's first candle and the latest closed candle (UTC, on the grid).
/// </summary>
public sealed record DownloadCandlesRequest(string? Venue, string? Symbol, string? Timeframe, DateTimeOffset? From, DateTimeOffset? To);