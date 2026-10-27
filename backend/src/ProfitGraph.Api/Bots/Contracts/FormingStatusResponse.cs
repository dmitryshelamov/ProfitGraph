namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     A change of a forming-candle feed's state; <c>Status</c> is <c>Live</c>, <c>NoData</c> or <c>Unsupported</c>.
/// </summary>
public sealed record FormingStatusResponse(string Venue, string Symbol, string Timeframe, string Status);