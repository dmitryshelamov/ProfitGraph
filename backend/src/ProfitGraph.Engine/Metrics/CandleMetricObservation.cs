namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Completed-candle accounting scalars for metrics without retaining or capturing account snapshots. Cost basis, reserved
///     quote and whether the position is open feed the capital cost metrics (ADR 0057).
/// </summary>
public readonly record struct CandleMetricObservation(
    long Sequence,
    int CandleIndex,
    decimal StartingCapital,
    decimal Equity,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal FeesBase,
    decimal FeesQuote,
    decimal CostBasis = 0m,
    decimal QuoteReserved = 0m,
    bool InPosition = false);