namespace ProfitGraph.Engine.Market;

/// <summary>
///     A half-open UTC range of candle open times on the timeframe grid in which the market was closed, so history has no
///     candle there (ADR 0023, TC1). Replay accepts a gap between two candles only where closures cover it.
/// </summary>
public sealed record MarketClosure(DateTimeOffset From, DateTimeOffset To);