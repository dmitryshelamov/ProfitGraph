namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Completed-candle accounting scalars for metrics without retaining or capturing account snapshots.
/// </summary>
public readonly record struct CandleMetricObservation(
    long Sequence,
    int CandleIndex,
    decimal StartingCapital,
    decimal Equity,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal FeesBase,
    decimal FeesQuote);