namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     What joining a forming-candle feed returns: the feed's state and the latest candle it holds, if any.
/// </summary>
public sealed record FormingFeedSnapshotResponse(string Venue, string Symbol, string Timeframe, string Status, FormingCandleResponse? Candle);