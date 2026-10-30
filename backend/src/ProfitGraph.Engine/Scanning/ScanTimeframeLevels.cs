namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The levels of one timeframe at a pair's price (ADR 0059, section 8): the price they apply at, the nearest support and
///     resistance and the timeframe's average range (ADR 0070), each empty without the candles it needs.
/// </summary>
public sealed record ScanTimeframeLevels(string Timeframe, decimal Price, decimal? Support, decimal? Resistance, decimal? AverageRange);