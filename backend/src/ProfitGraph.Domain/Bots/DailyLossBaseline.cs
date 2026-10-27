namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The daily loss baseline of a UTC day (ADR 0041): <paramref name="Equity" /> is the equity the day's loss is measured from, the
///     previous day's last recorded equity or, on the first day after a start, the first root's; <paramref name="LastEquity" /> is the
///     equity of the last recorded root, which the next day takes as its baseline.
/// </summary>
public sealed record DailyLossBaseline(DateOnly Day, decimal Equity, decimal LastEquity);