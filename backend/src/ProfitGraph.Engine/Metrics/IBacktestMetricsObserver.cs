using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Engine.Metrics;

/// <summary>
///     Requests scalar candle metrics instead of a full immutable candle and account projection.
/// </summary>
public interface IBacktestMetricsObserver : IBacktestObserver
{
    void OnCandleMetrics(CandleMetricObservation observation);
}