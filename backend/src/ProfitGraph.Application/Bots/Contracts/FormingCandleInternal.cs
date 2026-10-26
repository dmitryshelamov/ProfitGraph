namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The candle a venue is forming now, or has just closed, as a live chart shows it (ADR 0026). It lives in memory only
///     and is never stored, never reaches a backtest and is not what a bot decides on. A venue without a close flag reports
///     <see cref="IsClosed" /> as false; the next open time then ends the candle.
/// </summary>
public sealed record FormingCandleInternal(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    bool IsClosed,
    DateTimeOffset ObservedAt);