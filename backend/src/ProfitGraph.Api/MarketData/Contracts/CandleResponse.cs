namespace ProfitGraph.Api.MarketData.Contracts;

/// <summary>
///     A stored candle; prices and volume are exact decimal strings.
/// </summary>
public sealed record CandleResponse(DateTimeOffset OpenTime, string Open, string High, string Low, string Close, string Volume);