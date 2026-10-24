namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     A half-open UTC range of candle open times.
/// </summary>
public sealed record CandleRangeInternal(DateTimeOffset From, DateTimeOffset To);