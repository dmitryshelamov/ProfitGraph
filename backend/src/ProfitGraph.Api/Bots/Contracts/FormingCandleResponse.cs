namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The candle a venue is forming (or has just closed) in a bot's series, pushed over <c>/ws/forming-candles</c> (ADR 0026).
///     Prices and volume are exact decimal strings, times are UTC.
/// </summary>
public sealed record FormingCandleResponse(
    string Venue,
    string Symbol,
    string Timeframe,
    DateTimeOffset OpenTime,
    string Open,
    string High,
    string Low,
    string Close,
    string Volume,
    bool IsClosed,
    DateTimeOffset ObservedAt);