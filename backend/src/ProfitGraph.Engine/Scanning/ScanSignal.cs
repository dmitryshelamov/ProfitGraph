namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A pair entering a preset's result in a replay (ADR 0059): when, the entry price (the open of the next minute), the metrics at
///     that moment, how the price moved after it and the trades its scenarios took (ADR 0059, section 8). A horizon the history does
///     not reach yet is empty; a trade still open when the history ends is left out.
/// </summary>
public sealed record ScanSignal(
    string Symbol,
    DateTimeOffset At,
    decimal? EntryPrice,
    ScanMetrics Metrics,
    decimal? Change15m,
    decimal? Change1h,
    decimal? Change4h,
    decimal? Change24h,
    decimal? MaxRise24h,
    decimal? MaxDrop24h,
    IReadOnlyList<ScanTrade>? Trades);